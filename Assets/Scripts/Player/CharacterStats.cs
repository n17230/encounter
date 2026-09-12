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

    // Regen is paid out in discrete ticks rather than continuously: every
    // regenTickInterval seconds, rate x interval is added. Rates stay in
    // per-second units so gear/aura bonuses read naturally.
    [SerializeField] private float regenTickInterval = 5f;

    public Stat MaxHealth { get; private set; }
    public Stat HealthRegenRate { get; private set; }
    public Stat MaxMana { get; private set; }
    public Stat ManaRegenRate { get; private set; }
    public Stat RunSpeed { get; private set; }
    public Stat Armor { get; private set; }
    public Stat ManaCostMultiplier { get; private set; }
    public Stat ThreatMultiplier { get; private set; }
    public Stat DamageMultiplier { get; private set; }
    public Stat HealingMultiplier { get; private set; }
    public Stat DamageTakenMultiplier { get; private set; }
    public Stat WeaponDamageBonus { get; private set; }

    public readonly NetworkVariable<float> CurrentHealth =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> CurrentMana =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Absorb shield (e.g. Aegis of Arcane) - consumed in DealDamage before
    // health is touched. A new grant replaces the remainder rather than
    // adding to it - see GrantShield.
    public readonly NetworkVariable<float> ShieldAmount =
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
    private float nextRegenTick;

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
        ThreatMultiplier = new Stat(1f);
        DamageMultiplier = new Stat(1f);
        HealingMultiplier = new Stat(1f);
        DamageTakenMultiplier = new Stat(1f);
        WeaponDamageBonus = new Stat(0f);
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
            case StatType.ThreatMultiplier: return ThreatMultiplier;
            case StatType.DamageMultiplier: return DamageMultiplier;
            case StatType.HealingMultiplier: return HealingMultiplier;
            case StatType.DamageTakenMultiplier: return DamageTakenMultiplier;
            case StatType.WeaponDamageBonus: return WeaponDamageBonus;
            default: return null;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        CurrentHealth.Value = MaxHealth.Value;
        CurrentMana.Value = MaxMana.Value;
        nextRegenTick = Time.time + regenTickInterval;
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

        while (Time.time >= nextRegenTick)
        {
            nextRegenTick += regenTickInterval;
            if (isDead) continue;
            CurrentHealth.Value = Mathf.Min(MaxHealth.Value, CurrentHealth.Value + HealthRegenRate.Value * regenTickInterval);
            CurrentMana.Value = Mathf.Min(MaxMana.Value, CurrentMana.Value + ManaRegenRate.Value * regenTickInterval);
        }

        effects.Tick(Time.time, TickEffect);
    }

    // The one way anything hostile reaches a character. Server-only.
    public void ReceiveHit(in HitInfo hit)
    {
        if (!IsServer) return;

        if (hit.Damage > 0f) DealDamage(hit.Damage, hit.AttackerClientId);
        if (hit.Heal > 0f) Heal(hit.Heal, hit.AttackerClientId);
        if (hit.ShieldAmount > 0f) GrantShield(hit.ShieldAmount);
        if (hit.ExtraThreat > 0f) AddThreat(hit.ExtraThreat, hit.AttackerClientId);

        if (hit.Effect != null) ApplyEffect(hit.Effect, hit.EffectDuration, hit.AttackerClientId, hit.Source);
    }

    // Replaces any existing shield outright - two shields don't stack,
    // matching the same "buff override" philosophy as
    // EffectStackingMode.Override. Server-only.
    public void GrantShield(float amount)
    {
        if (!IsServer) return;
        ShieldAmount.Value = amount;
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

    // Ends one specific effect on this character early (e.g. One For All
    // moving from a previous target to a new one). Server-only.
    public void RemoveEffect(StatusEffectData effect)
    {
        if (!IsServer) return;
        effects.Remove(effect);
    }

    // Strips one currently active negative effect (StatusEffectData
    // .IsNegative), whichever is found first if more than one is active -
    // e.g. Cleanse. Server-only. Returns true if something was removed.
    public bool RemoveOneNegativeEffect()
    {
        if (!IsServer) return false;

        StatusEffectData found = null;
        foreach (StatusEffectTracker.ActiveEffect active in effects.All)
        {
            if (!active.Data.IsNegative) continue;
            found = active.Data;
            break; // can't call effects.Remove while still enumerating effects.All
        }
        return found != null && effects.Remove(found);
    }

    // True while any active effect (e.g. Trample, Seismic Slam) has
    // IsStun set - movement/casting/attacking should all be blocked. Only
    // EnemyAI currently checks this; no ability stuns a player yet.
    public bool IsStunned
    {
        get
        {
            foreach (StatusEffectTracker.ActiveEffect active in effects.All)
            {
                if (active.Data.IsStun) return true;
            }
            return false;
        }
    }

    private void DealDamage(float rawDamage, ulong attackerClientId)
    {
        CharacterStats attacker = AttackerStats(attackerClientId);
        if (attacker != null) rawDamage *= attacker.DamageMultiplier.Value;

        float mitigated = rawDamage * (1f - Mathf.Clamp01(Armor.Value / 100f));
        mitigated *= DamageTakenMultiplier.Value;

        // An absorb shield (e.g. Aegis of Arcane) intercepts damage before
        // it can be redirected or reduce health - "the next N damage" is
        // prevented outright, not healed back afterward. Threat below is
        // based on what's left after this, so a fully-absorbed hit
        // generates none.
        if (ShieldAmount.Value > 0f)
        {
            float absorbed = Mathf.Min(ShieldAmount.Value, mitigated);
            ShieldAmount.Value -= absorbed;
            mitigated -= absorbed;
        }

        // A redirect (e.g. One For All) siphons part of the remaining
        // damage straight to another character's health, with no
        // re-mitigation and no threat of its own - threat below is still
        // based on the full (post-shield) mitigated amount, unaffected by
        // where the health loss actually lands.
        float selfDamage = mitigated;
        if (TryGetActiveRedirect(out float redirectPercent, out ulong redirectToClientId))
        {
            CharacterStats redirectTarget = AttackerStats(redirectToClientId);
            if (redirectTarget != null && redirectTarget != this)
            {
                float redirected = mitigated * redirectPercent;
                selfDamage = mitigated - redirected;
                redirectTarget.ApplyRawDamage(redirected);
            }
        }

        ApplyRawDamage(selfDamage);

        // 1 threat per 1 point of damage actually dealt (post-mitigation).
        AddThreat(mitigated, attackerClientId);
    }

    // Reduces health and fires OnDeath if needed, with no mitigation,
    // multipliers or threat - used both for a character's own damage and
    // for damage a redirect effect hands off to someone else.
    private void ApplyRawDamage(float amount)
    {
        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - amount);
        if (CurrentHealth.Value <= 0f && !isDead)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }

    // The strongest currently-active redirect on THIS character, if any -
    // stacking multiple redirect effects wasn't asked for, so only one
    // applies at a time.
    private bool TryGetActiveRedirect(out float percent, out ulong redirectToClientId)
    {
        percent = 0f;
        redirectToClientId = NoAttacker;
        foreach (StatusEffectTracker.ActiveEffect active in effects.All)
        {
            if (active.Data.DamageRedirectPercent <= percent) continue;
            percent = active.Data.DamageRedirectPercent;
            redirectToClientId = active.AttackerClientId;
        }
        return percent > 0f;
    }

    // Threat is scaled by the attacker's own ThreatMultiplier (gear).
    private void AddThreat(float amount, ulong attackerClientId)
    {
        if (attackerClientId == NoAttacker || threatTable == null) return;

        CharacterStats attacker = AttackerStats(attackerClientId);
        float multiplier = attacker != null ? attacker.ThreatMultiplier.Value : 1f;
        threatTable.AddThreat(attackerClientId, amount * multiplier);
    }

    // The attacking player's stats, for outgoing multipliers (null for mobs
    // and environmental damage).
    private CharacterStats AttackerStats(ulong attackerClientId)
    {
        if (attackerClientId == NoAttacker) return null;
        if (!NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out NetworkClient client)) return null;
        if (client.PlayerObject == null) return null;
        return client.PlayerObject.TryGetComponent(out CharacterStats attacker) ? attacker : null;
    }

    private void TickEffect(StatusEffectTracker.ActiveEffect effect)
    {
        if (effect.Data.TickDamage > 0f) DealDamage(effect.Data.TickDamage, effect.AttackerClientId);
        if (effect.Data.TickHeal > 0f) Heal(effect.Data.TickHeal, effect.AttackerClientId);
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
        ShieldAmount.Value = 0f;
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

    // healerClientId (optional) looks up the healer's own HealingMultiplier
    // (gear) and scales the amount by it, mirroring how DealDamage applies
    // the attacker's DamageMultiplier. NoAttacker (the default) = no
    // scaling, for regen ticks and other sourceless healing.
    public void Heal(float amount, ulong healerClientId = NoAttacker)
    {
        if (!IsServer) return;
        CharacterStats healer = AttackerStats(healerClientId);
        if (healer != null) amount *= healer.HealingMultiplier.Value;

        CurrentHealth.Value = Mathf.Min(MaxHealth.Value, CurrentHealth.Value + amount);
    }

    // Read-only affordability check (ManaCostMultiplier applied), for
    // gating whether a cast is even allowed to start - it spends nothing,
    // so callers must still call TrySpendMana at the point the cast
    // actually succeeds.
    public bool HasEnoughMana(float baseCost)
    {
        return CurrentMana.Value >= baseCost * ManaCostMultiplier.Value;
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
