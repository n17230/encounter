using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterStats : NetworkBehaviour
{
    private class ActiveDebuff
    {
        public float Magnitude;
        public float TickInterval;
        public float NextTickTime;
        public float ExpireTime;
        public ulong AttackerClientId;
    }

    // Sentinel meaning "no attacker" - damage from a source that shouldn't
    // generate threat (environmental, or callers that don't track a caster).
    public const ulong NoAttacker = ulong.MaxValue;

    private static readonly object SlowModifierSource = new object();

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

    public readonly NetworkVariable<float> CurrentHealth =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<float> CurrentMana =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly Dictionary<DebuffType, ActiveDebuff> activeDebuffs = new Dictionary<DebuffType, ActiveDebuff>();
    private bool isDead;
    private ThreatTable threatTable;

    public event System.Action OnDeath;

    private void Awake()
    {
        MaxHealth = new Stat(baseMaxHealth);
        HealthRegenRate = new Stat(baseHealthRegenRate);
        MaxMana = new Stat(baseMaxMana);
        ManaRegenRate = new Stat(baseManaRegenRate);
        RunSpeed = new Stat(baseRunSpeed);
        Armor = new Stat(baseArmor);
        threatTable = GetComponent<ThreatTable>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        CurrentHealth.Value = MaxHealth.Value;
        CurrentMana.Value = MaxMana.Value;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (CurrentHealth.Value < MaxHealth.Value)
        {
            CurrentHealth.Value = Mathf.Min(MaxHealth.Value, CurrentHealth.Value + HealthRegenRate.Value * Time.fixedDeltaTime);
        }
        if (CurrentMana.Value < MaxMana.Value)
        {
            CurrentMana.Value = Mathf.Min(MaxMana.Value, CurrentMana.Value + ManaRegenRate.Value * Time.fixedDeltaTime);
        }

        ProcessDebuffs();
    }

    public void ApplyDamage(float rawDamage, ulong attackerClientId = NoAttacker)
    {
        if (!IsServer) return;
        float mitigated = rawDamage * (1f - Mathf.Clamp01(Armor.Value / 100f));
        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - mitigated);

        if (attackerClientId != NoAttacker)
        {
            threatTable?.AddThreat(attackerClientId, mitigated);
        }

        if (CurrentHealth.Value <= 0f && !isDead)
        {
            isDead = true;
            OnDeath?.Invoke();
        }
    }

    // For threat generated independent of damage (e.g. a taunt with 0
    // damage but a large ThreatValue) - separate from the automatic 1
    // threat-per-1-damage path in ApplyDamage, so the two stack additively
    // rather than one replacing the other.
    public void AddThreat(float amount, ulong attackerClientId)
    {
        if (!IsServer) return;
        if (attackerClientId == NoAttacker) return;
        threatTable?.AddThreat(attackerClientId, amount);
    }

    public void RestoreFull()
    {
        if (!IsServer) return;
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

    public bool TrySpendMana(float amount)
    {
        if (!IsServer) return false;
        if (CurrentMana.Value < amount) return false;
        CurrentMana.Value -= amount;
        return true;
    }

    public void ApplyDebuff(DebuffType type, float magnitude, float tickInterval, float duration, ulong attackerClientId = NoAttacker)
    {
        if (!IsServer) return;
        if (type == DebuffType.None) return;

        float now = Time.time;

        if (activeDebuffs.TryGetValue(type, out ActiveDebuff existing))
        {
            // General rule (more of these cases are coming): a weaker
            // reapplication of the same debuff type must never downgrade an
            // already-stronger one. Duration and magnitude are judged
            // independently - each only ever moves up, never down. This is
            // what stops e.g. an ice patch's shorter/weaker tick from
            // cutting short or diluting a frostbolt's longer/stronger
            // direct-hit debuff. TickInterval is deliberately left alone on
            // refresh either way (same invariant as before: a refresh never
            // touches NextTickTime, so a due tick always still fires).
            float candidateExpireTime = now + duration;
            bool durationImproved = candidateExpireTime > existing.ExpireTime;
            bool magnitudeImproved = magnitude > existing.Magnitude;

            if (durationImproved) existing.ExpireTime = candidateExpireTime;

            if (magnitudeImproved)
            {
                if (type == DebuffType.Slow)
                {
                    // The active StatModifier was added with the old
                    // magnitude and is immutable - swap it for one with the
                    // new, stronger magnitude rather than just bumping the
                    // stored value (which alone wouldn't affect RunSpeed).
                    RunSpeed.RemoveAllModifiersFromSource(SlowModifierSource);
                    RunSpeed.AddModifier(new StatModifier(-magnitude, StatModifierType.PercentAdditive, SlowModifierSource));
                }
                existing.Magnitude = magnitude;
            }

            if (durationImproved || magnitudeImproved) existing.AttackerClientId = attackerClientId;

            return;
        }

        activeDebuffs[type] = new ActiveDebuff
        {
            Magnitude = magnitude,
            TickInterval = tickInterval,
            NextTickTime = now,
            ExpireTime = now + duration,
            AttackerClientId = attackerClientId
        };

        if (type == DebuffType.Slow)
        {
            RunSpeed.AddModifier(new StatModifier(-magnitude, StatModifierType.PercentAdditive, SlowModifierSource));
        }
    }

    private void ProcessDebuffs()
    {
        if (activeDebuffs.Count == 0) return;

        float now = Time.time;
        List<DebuffType> expired = null;

        foreach (KeyValuePair<DebuffType, ActiveDebuff> entry in activeDebuffs)
        {
            DebuffType type = entry.Key;
            ActiveDebuff debuff = entry.Value;

            if (type == DebuffType.Burn && now >= debuff.NextTickTime)
            {
                ApplyDamage(debuff.Magnitude, debuff.AttackerClientId);
                debuff.NextTickTime += debuff.TickInterval;
            }

            if (now >= debuff.ExpireTime)
            {
                (expired ??= new List<DebuffType>()).Add(type);
            }
        }

        if (expired == null) return;

        foreach (DebuffType type in expired)
        {
            activeDebuffs.Remove(type);
            if (type == DebuffType.Slow)
            {
                RunSpeed.RemoveAllModifiersFromSource(SlowModifierSource);
            }
        }
    }
}
