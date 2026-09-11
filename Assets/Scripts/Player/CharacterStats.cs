using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterStats : NetworkBehaviour
{
    // Sentinel meaning "no attacker" - damage from a source that shouldn't
    // generate threat (environmental, or callers that don't track a caster).
    public const ulong NoAttacker = ulong.MaxValue;

    [SerializeField] private float baseMaxHealth = 100f;
    [SerializeField] private float baseHealthRegenRate = 2f;
    [SerializeField] private float baseMaxMana = 100f;
    [SerializeField] private float baseManaRegenRate = 5f;
    [SerializeField] private float baseRunSpeed = 6f;
    [SerializeField] private float baseArmor = 0f;

    public Stat MaxHealth { get; private set; }
    public Stat HealthRegenRate { get; private set; }
    public Stat MaxMana { get; private set; }
    public Stat ManaRegenRate { get; private set; }
    public Stat RunSpeed { get; private set; }
    public Stat Armor { get; private set; }
    public Stat ManaCostMultiplier { get; private set; }

    public readonly NetworkVariable<float> CurrentHealth =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> CurrentMana =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // The Stat objects above only carry modifiers on the server (gear and
    // effects are applied there). Clients need the resulting values for the
    // HUD and, for the owner, for local movement - so the server mirrors
    // them out here whenever they change.
    public readonly NetworkVariable<float> SyncedMaxHealth =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> SyncedMaxMana =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> SyncedRunSpeed =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> SyncedManaCostMultiplier =
        new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Client-visible mirror of the active status effects (name + expiry),
    // for UI only. The authoritative state is the server-side tracker.
    public readonly NetworkList<ActiveEffectNet> ActiveEffects = new NetworkList<ActiveEffectNet>();

    private readonly StatusEffectTracker effects = new StatusEffectTracker();
    private readonly List<(EffectImmunity rule, object source)> immunities = new List<(EffectImmunity, object)>();
    private bool isDead;
    private ThreatTable threatTable;

    public event Action OnDeath;

    private void Awake()
    {
        MaxHealth = new Stat(baseMaxHealth);
        HealthRegenRate = new Stat(baseHealthRegenRate);
        MaxMana = new Stat(baseMaxMana);
        ManaRegenRate = new Stat(baseManaRegenRate);
        RunSpeed = new Stat(baseRunSpeed);
        Armor = new Stat(baseArmor);
        ManaCostMultiplier = new Stat(1f);
        threatTable = GetComponent<ThreatTable>();

        effects.Applied += HandleEffectApplied;
        effects.Refreshed += SyncEffect;
        effects.Expired += HandleEffectExpired;
    }

    public Stat GetStat(StatType type)
    {
        switch (type)
        {
            case StatType.MaxHealth: return MaxHealth;
            case StatType.HealthRegenRate: return HealthRegenRate;
            case StatType.MaxMana: return MaxMana;
            case StatType.ManaRegenRate: return ManaRegenRate;
            case StatType.RunSpeed: return RunSpeed;
            case StatType.Armor: return Armor;
            case StatType.ManaCostMultiplier: return ManaCostMultiplier;
            default: return null;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        CurrentHealth.Value = MaxHealth.Value;
        CurrentMana.Value = MaxMana.Value;
        SyncDerivedStats();
    }

    private void SyncDerivedStats()
    {
        if (SyncedMaxHealth.Value != MaxHealth.Value) SyncedMaxHealth.Value = MaxHealth.Value;
        if (SyncedMaxMana.Value != MaxMana.Value) SyncedMaxMana.Value = MaxMana.Value;
        if (SyncedRunSpeed.Value != RunSpeed.Value) SyncedRunSpeed.Value = RunSpeed.Value;
        if (SyncedManaCostMultiplier.Value != ManaCostMultiplier.Value) SyncedManaCostMultiplier.Value = ManaCostMultiplier.Value;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        SyncDerivedStats();

        if (CurrentHealth.Value < MaxHealth.Value)
        {
            CurrentHealth.Value = Mathf.Min(MaxHealth.Value, CurrentHealth.Value + HealthRegenRate.Value * Time.fixedDeltaTime);
        }
        if (CurrentMana.Value < MaxMana.Value)
        {
            CurrentMana.Value = Mathf.Min(MaxMana.Value, CurrentMana.Value + ManaRegenRate.Value * Time.fixedDeltaTime);
        }

        effects.Tick(Time.time, TickEffect);
    }

    // The one way anything hostile reaches a character. Server-only.
    public void ReceiveHit(in HitInfo hit)
    {
        if (!IsServer) return;

        if (hit.Damage > 0f) DealDamage(hit.Damage, hit.AttackerClientId);
        if (hit.ExtraThreat > 0f) AddThreat(hit.ExtraThreat, hit.AttackerClientId);

        if (hit.Effect != null) ApplyEffect(hit.Effect, hit.EffectDuration, hit.AttackerClientId, hit.Source);
    }

    // Non-hostile application too (auras, buffs). duration <= 0 uses the
    // effect's own Duration. Server-only.
    public void ApplyEffect(StatusEffectData effect, float duration, ulong attackerClientId, HitSource source)
    {
        if (!IsServer || effect == null) return;
        if (IsImmune(effect, source)) return;

        effects.Apply(effect, duration > 0f ? duration : effect.Duration, attackerClientId, Time.time);
    }

    // Immunities are keyed by source the same way modifiers are, so gear
    // can grant and revoke them cleanly on equip/unequip.
    public void AddImmunity(EffectImmunity rule, object source)
    {
        immunities.Add((rule, source));
    }

    public void RemoveImmunitiesFromSource(object source)
    {
        immunities.RemoveAll(entry => Equals(entry.source, source));
    }

    public bool IsImmune(StatusEffectData effect, HitSource source)
    {
        foreach ((EffectImmunity rule, object _) in immunities)
        {
            if (rule.Blocks(effect, source)) return true;
        }
        return false;
    }

    private void DealDamage(float rawDamage, ulong attackerClientId)
    {
        float mitigated = rawDamage * (1f - Mathf.Clamp01(Armor.Value / 100f));
        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - mitigated);

        // 1 threat per 1 point of damage actually dealt (post-mitigation).
        AddThreat(mitigated, attackerClientId);

        if (CurrentHealth.Value <= 0f && !isDead)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }

    private void AddThreat(float amount, ulong attackerClientId)
    {
        if (attackerClientId == NoAttacker) return;
        threatTable?.AddThreat(attackerClientId, amount);
    }

    private void TickEffect(StatusEffectTracker.ActiveEffect effect)
    {
        DealDamage(effect.Data.TickDamage, effect.AttackerClientId);
    }

    private void HandleEffectApplied(StatusEffectTracker.ActiveEffect effect)
    {
        foreach (StatBonus bonus in effect.Data.Modifiers)
        {
            GetStat(bonus.Stat)?.AddModifier(new StatModifier(bonus.Value, bonus.ModifierType, effect.Data));
        }
        SyncEffect(effect);
    }

    private void HandleEffectExpired(StatusEffectTracker.ActiveEffect effect)
    {
        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            GetStat(type)?.RemoveAllModifiersFromSource(effect.Data);
        }

        for (int i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            if (ActiveEffects[i].EffectId == effect.Data.Id) ActiveEffects.RemoveAt(i);
        }
    }

    private void SyncEffect(StatusEffectTracker.ActiveEffect effect)
    {
        ActiveEffectNet entry = new ActiveEffectNet
        {
            EffectId = effect.Data.Id,
            ExpireServerTime = NetworkManager.ServerTime.Time + (effect.ExpireTime - Time.time),
        };

        for (int i = 0; i < ActiveEffects.Count; i++)
        {
            if (ActiveEffects[i].EffectId == entry.EffectId)
            {
                ActiveEffects[i] = entry;
                return;
            }
        }
        ActiveEffects.Add(entry);
    }

    public void RestoreFull()
    {
        if (!IsServer) return;
        effects.ClearAll();
        CurrentHealth.Value = MaxHealth.Value;
        CurrentMana.Value = MaxMana.Value;
        isDead = false;
    }

    // For when a max stat drops (e.g. unequipping +MaxHealth gear) so the
    // current value can't sit above the new ceiling.
    public void ClampToMax()
    {
        if (!IsServer) return;
        CurrentHealth.Value = Mathf.Min(CurrentHealth.Value, MaxHealth.Value);
        CurrentMana.Value = Mathf.Min(CurrentMana.Value, MaxMana.Value);
    }

    public void Heal(float amount)
    {
        if (!IsServer) return;
        CurrentHealth.Value = Mathf.Min(MaxHealth.Value, CurrentHealth.Value + amount);
    }

    // baseCost is the ability's listed cost; the character's ManaCostMultiplier
    // (gear) is applied here so every caller pays the discounted price.
    public bool TrySpendMana(float baseCost)
    {
        if (!IsServer) return false;
        float cost = baseCost * ManaCostMultiplier.Value;
        if (CurrentMana.Value < cost) return false;
        CurrentMana.Value -= cost;
        return true;
    }
}
