using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
public class EnemyAI : NetworkBehaviour
{
    [SerializeField] private TargetingMode targetingMode = TargetingMode.HighestThreat;
    [SerializeField] private WeaponData mainHandWeapon;
    [SerializeField] private WeaponData offHandWeapon;
    [SerializeField] private float mainHandDamageModifier = 1f;
    [SerializeField] private float offHandDamageModifier = 0.9f;
    [SerializeField] private float attackInterval = 1.5f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float deathDespawnDelay = 2f;

    private const float ManaOrbDropChance = 0.05f;

    // --- Skeleton Tactician escort encounter (see BOSS_DESIGN.md) ---
    // None (the default) leaves every other mob (Goblin, Ogre, ...)
    // completely unaffected by anything below. All the numeric defaults
    // here already match the finalized design, so a prefab only needs to
    // override MobRole itself plus whichever asset REFERENCES it needs
    // (weapons/effects can't have a meaningful C# default) - see the five
    // MobSkeleton*.prefab files.
    [Header("Skeleton Escort")]
    [SerializeField] private MobRole mobRole = MobRole.None;

    // How far an escort member must be from an alive Tactician to receive
    // Tactical Instruction (mana regen + targeting priority + chill
    // immunity) - read off the TACTICIAN instance, not the escort member.
    [SerializeField] private float tacticalInstructionRange = 120f;
    // The effect Tactical Instruction grants immunity to (Icebolt's Slow).
    [SerializeField] private StatusEffectData chillImmunityEffect;

    // Warrior
    [SerializeField] private StatusEffectData warriorStunEffect;
    [SerializeField] private float warriorStunDuration = 1.5f;
    [SerializeField] private float warriorStunCooldown = 14f;
    [SerializeField] private StatusEffectData warriorEnrageEffect;
    [SerializeField] private float warriorEnrageDuration = 12f;
    [SerializeField] private float warriorEnrageTriggerRange = 20f;
    [SerializeField] private float warriorEnrageHeal = 200f;
    [SerializeField] private float warriorTacticianPriorityRange = 40f;

    // Archer
    [SerializeField] private StatusEffectData archerSlowEffect;
    [SerializeField] private float tendonShotCost = 100f;
    [SerializeField] private float tendonShotCooldown = 20f;
    [SerializeField] private StatusEffectData archerVisionEffect;
    [SerializeField] private float concussiveShotCost = 150f;
    [SerializeField] private float concussiveShotCooldown = 35f;
    [SerializeField] private float concussiveShotHpThreshold = 0.6f;
    [SerializeField] private float archerRepositionDistance = 20f;
    [SerializeField] private float archerMp5Bonus = 15f;

    // Healer - each spell carries its own range rather than one shared
    // Healer-wide value, even though they're all 50 right now.
    [SerializeField] private StatusEffectData healerHotEffect;
    [SerializeField] private float underworldGuardianCost = 100f;
    [SerializeField] private float underworldGuardianHpThreshold = 0.95f;
    [SerializeField] private float underworldGuardianRange = 50f;
    [SerializeField] private float persistenceCost = 100f;
    [SerializeField] private float persistenceCastTime = 1.5f;
    [SerializeField] private float persistenceHeal = 300f;
    [SerializeField] private float persistenceHpThreshold = 0.55f;
    [SerializeField] private float persistenceRange = 50f;
    [SerializeField] private float manaLeechCost = 75f;
    [SerializeField] private float manaLeechAmount = 150f;
    [SerializeField] private float manaLeechManaThreshold = 150f;
    [SerializeField] private float manaLeechRange = 50f;
    [SerializeField] private float healerMp5Bonus = 25f;

    // Mage
    [SerializeField] private StatusEffectData mageEnshroudEffect;
    [SerializeField] private float enshroudCost = 200f;
    [SerializeField] private float enshroudCooldown = 40f;
    [SerializeField] private float enshroudTriggerRange = 40f;
    // Not given an explicit number by design - flagged placeholder, see
    // review_with_fable.md.
    [SerializeField] private float arcaneShieldCooldown = 30f;
    [SerializeField] private float arcaneShieldCost = 200f;
    [SerializeField] private float arcaneShieldRadius = 15f;
    [SerializeField] private float arcaneShieldDuration = 8f;
    [SerializeField] private float arcaneShieldHpThreshold = 0.5f;
    [SerializeField] private StatusEffectData arcaneShieldBuffEffect;
    [SerializeField] private float mageMp5Bonus = 25f;

    // Tactician
    [SerializeField] private WeaponData tacticianMeleeWeapon;
    [SerializeField] private WeaponData tacticianRangedWeapon;
    [SerializeField] private float tacticianMeleeEngageRange = 15f;

    private CharacterStats currentTarget;
    private bool tacticianEngaged;
    private bool tacticalInstructionActive;
    private bool persistenceCasting;
    private float nextStunTime;
    private float nextTendonShotTime;
    private float nextConcussiveShotTime;
    private float nextEnshroudTime;
    private float nextArcaneShieldTime;

    private CharacterController controller;
    private CharacterStats stats;
    private ThreatTable threatTable;
    private Animator animator;
    private float verticalVelocity;
    private float nextAttackTime;
    private bool nextAttackIsOffHand;
    private bool isDead;
    private readonly List<TargetCandidate<CharacterStats>> candidates = new List<TargetCandidate<CharacterStats>>();

    // Server-initiated external movement (e.g. Vacuum's pull) - overrides
    // normal targeting/movement for the window. Mobs are fully
    // server-driven already, so no client notification is needed.
    private Vector3 pullTargetPosition;
    private float pullSpeed;
    private float pullEndTime;
    private bool IsPulling => Time.time < pullEndTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stats = GetComponent<CharacterStats>();
        threatTable = GetComponent<ThreatTable>();
        // Optional - mobs whose visual has no Animator (or no visual at
        // all, e.g. the fallback capsule) just skip all animation calls.
        animator = GetComponentInChildren<Animator>();

        // NetworkAnimator lives on the shared MobNPC base, but each mob's
        // actual Animator is on a different nested visual child - so it
        // can't be wired once via a prefab override. Assign it here instead.
        NetworkAnimator networkAnimator = GetComponent<NetworkAnimator>();
        if (networkAnimator != null && animator != null)
        {
            networkAnimator.Animator = animator;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        stats.OnDeath += HandleDeath;

        // The Tactician itself is always chill-immune, not just its escort
        // (which get it conditionally, via UpdateTacticalInstruction, while
        // its aura reaches them).
        if (mobRole == MobRole.SkeletonTactician && chillImmunityEffect != null)
        {
            stats.AddImmunity(new EffectImmunity { Effect = chillImmunityEffect, GroundOnly = false }, this);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) stats.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        isDead = true;
        if (animator != null) animator.SetTrigger("death");
        if (Random.value < ManaOrbDropChance) ManaOrb.TrySpawn(transform.position);
        StartCoroutine(DespawnAfterDelay());
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(deathDespawnDelay);
        NetworkObject.Despawn();
    }

    // See PlayerMovement.ServerBeginPull - same direction-agnostic contract.
    public void ServerBeginPull(Vector3 towardPosition, float speed, float duration)
    {
        if (!IsServer) return;
        pullTargetPosition = towardPosition;
        pullSpeed = speed;
        pullEndTime = Time.time + duration;
    }

    // Called server-side (e.g. by a resolving Recall cast) to instantly
    // move this mob to position. No validator/RPC concerns here - mobs are
    // fully server-driven, so this just relocates them like a respawn would.
    public void ServerTeleportTo(Vector3 position)
    {
        if (!IsServer) return;
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        verticalVelocity = 0f;
    }

    private void FixedUpdate()
    {
        if (!IsServer || isDead) return;

        if (stats.IsStunned)
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);
            return;
        }

        if (IsPulling)
        {
            Vector3 toPullTarget = pullTargetPosition - transform.position;
            toPullTarget.y = 0f;
            bool arrived = toPullTarget.magnitude < 0.3f;
            if (arrived) pullEndTime = Time.time;

            Vector3 pullVelocity = arrived ? Vector3.zero : toPullTarget.normalized * pullSpeed;
            MoveWithGravity(pullVelocity);
            if (animator != null) animator.SetFloat("speed", arrived ? 0f : 1f);
            return;
        }

        UpdateTacticalInstruction();

        if (mobRole == MobRole.SkeletonTactician)
        {
            TickTacticianBehavior();
            return;
        }

        CharacterStats target = FindTarget();
        currentTarget = target;

        // Pure support, no weapon of its own - holds position rather than
        // chasing a player it can never hurt. It still tracks a target
        // (Mana Leech needs one) via the normal selector above.
        if (mobRole == MobRole.SkeletonHealer)
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);
            TickHealerAbilities();
            return;
        }

        if (target == null)
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        // Cadence and reach come from the main-hand weapon when there is
        // one; the serialized interval and the shared melee constant are
        // the fallbacks for unarmed mobs.
        float cadence = mainHandWeapon != null ? mainHandWeapon.SwingInterval : attackInterval;
        float range = mainHandWeapon != null ? mainHandWeapon.Range : WeaponData.BasicAttackRange;

        if (distance > range)
        {
            Vector3 moveDirection = toTarget.normalized;
            transform.rotation = Quaternion.LookRotation(moveDirection);
            MoveWithGravity(moveDirection * stats.RunSpeed.Value);
            if (animator != null) animator.SetFloat("speed", 1f);
        }
        else
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);

            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + cadence;
                if (animator != null) animator.SetTrigger("attack");

                // Single attack cadence (attackInterval) with each swing
                // alternating hands. "Dual-wielding" isn't a separate flag -
                // it's just whether offHandWeapon is assigned. A mob with
                // only a main-hand weapon always swings with that alone.
                // The same weapon asset can be used in both hands - the
                // main/off-hand damage difference comes from these per-mob
                // modifiers, not a duplicated/re-tuned weapon asset.
                WeaponData weapon = mainHandWeapon;
                float modifier = mainHandDamageModifier;
                if (offHandWeapon != null)
                {
                    bool useOffHand = nextAttackIsOffHand;
                    weapon = useOffHand ? offHandWeapon : mainHandWeapon;
                    modifier = useOffHand ? offHandDamageModifier : mainHandDamageModifier;
                    nextAttackIsOffHand = !nextAttackIsOffHand;
                }

                if (weapon != null)
                {
                    target.ReceiveHit(new HitInfo
                    {
                        Damage = weapon.Damage * modifier,
                        AttackerClientId = CharacterStats.NoAttacker,
                        Attacker = stats,
                        Source = HitSource.Melee,
                        Effect = weapon.Effect,
                    });
                }
            }
        }

        switch (mobRole)
        {
            case MobRole.SkeletonWarrior: TickWarriorAbilities(target); break;
            case MobRole.SkeletonArcher: TickArcherAbilities(target); break;
            case MobRole.SkeletonMage: TickMageAbilities(); break;
        }
    }

    // Every connected, alive player is a candidate - including ones with no
    // threat entry yet (counted as 0), which is what lets LowestThreat single
    // out someone who hasn't engaged at all. Warrior/Archer/Mage swap to a
    // Tactical-Instruction-driven priority while an alive Tactician's aura
    // reaches them; everyone else (and any of those three once it doesn't)
    // just uses their own configured targetingMode.
    private CharacterStats FindTarget()
    {
        if (tacticalInstructionActive)
        {
            EnemyAI tactician = FindAliveTactician();
            switch (mobRole)
            {
                case MobRole.SkeletonWarrior:
                    CharacterStats nearTactician = tactician != null
                        ? FindTargetWithinRangeOf(tactician.transform.position, warriorTacticianPriorityRange)
                        : null;
                    if (nearTactician != null) return nearTactician;
                    break;
                case MobRole.SkeletonArcher:
                    return FindTargetByMode(TargetingMode.FarthestPlayer);
                case MobRole.SkeletonMage:
                    CharacterStats nearArchers = FindTargetNearestAllies(MobRole.SkeletonArcher);
                    if (nearArchers != null) return nearArchers;
                    break;
            }
        }

        return FindTargetByMode(targetingMode);
    }

    private CharacterStats FindTargetByMode(TargetingMode mode)
    {
        candidates.Clear();
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;

            float threat = 0f;
            threatTable?.ThreatByClientId.TryGetValue(client.ClientId, out threat);

            candidates.Add(new TargetCandidate<CharacterStats>
            {
                Subject = candidateStats,
                Threat = threat,
                Distance = Vector3.Distance(transform.position, candidateStats.transform.position),
            });
        }

        return TargetSelector.Select(candidates, mode, threatTable != null);
    }

    // Nearest-to-self alive player within range of the given point (e.g.
    // "within 40 yards of the Tactician") - null if nobody qualifies.
    private CharacterStats FindTargetWithinRangeOf(Vector3 point, float range)
    {
        CharacterStats best = null;
        float bestDistToSelf = float.MaxValue;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;
            if (Vector3.Distance(point, candidateStats.transform.position) > range) continue;

            float distToSelf = Vector3.Distance(transform.position, candidateStats.transform.position);
            if (distToSelf < bestDistToSelf)
            {
                bestDistToSelf = distToSelf;
                best = candidateStats;
            }
        }
        return best;
    }

    // Whichever alive player is nearest to ANY currently-alive ally of the
    // given role (e.g. "closest to the archers") - null if there are no
    // such allies or no valid players.
    private CharacterStats FindTargetNearestAllies(MobRole allyRole)
    {
        List<EnemyAI> allies = FindAllAllies(allyRole);
        if (allies.Count == 0) return null;

        CharacterStats best = null;
        float bestDist = float.MaxValue;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;

            foreach (EnemyAI ally in allies)
            {
                float dist = Vector3.Distance(ally.transform.position, candidateStats.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidateStats;
                }
            }
        }
        return best;
    }

    private CharacterStats FindNearestAlivePlayer(Vector3 position, float withinRange)
    {
        CharacterStats best = null;
        float bestDist = withinRange;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;
            float dist = Vector3.Distance(position, candidateStats.transform.position);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = candidateStats;
            }
        }
        return best;
    }

    private static EnemyAI FindAliveTactician()
    {
        foreach (EnemyAI mob in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (mob.mobRole == MobRole.SkeletonTactician && !mob.isDead) return mob;
        }
        return null;
    }

    private static List<EnemyAI> FindAllAllies(MobRole role)
    {
        List<EnemyAI> result = new List<EnemyAI>();
        foreach (EnemyAI mob in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (mob.mobRole == role && !mob.isDead) result.Add(mob);
        }
        return result;
    }

    private EnemyAI FindNearestAlly(MobRole role)
    {
        EnemyAI best = null;
        float bestDist = float.MaxValue;
        foreach (EnemyAI mob in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (mob == this || mob.mobRole != role || mob.isDead) continue;
            float dist = Vector3.Distance(transform.position, mob.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = mob;
            }
        }
        return best;
    }

    // The lowest-health escort member (any role, Tactician included) below
    // the given fraction and within maxRange (unlimited by default, for
    // callers - like Arcane Shield - that don't have their own range yet) -
    // optionally excluding anyone who already carries requireEffectMissing
    // (e.g. don't re-cast a HoT that's already up).
    private EnemyAI FindAllyBelowThreshold(float hpFractionThreshold, StatusEffectData requireEffectMissing, float maxRange = float.MaxValue)
    {
        EnemyAI best = null;
        float bestFraction = hpFractionThreshold;
        foreach (EnemyAI mob in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (mob.mobRole == MobRole.None || mob.isDead || mob.stats.MaxHealth.Value <= 0f) continue;
            float fraction = mob.stats.CurrentHealth.Value / mob.stats.MaxHealth.Value;
            if (fraction >= hpFractionThreshold) continue;
            if (requireEffectMissing != null && mob.stats.HasActiveEffect(requireEffectMissing)) continue;
            if (Vector3.Distance(transform.position, mob.transform.position) > maxRange) continue;
            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                best = mob;
            }
        }
        return best;
    }

    private int CountAliveEscort()
    {
        int count = 0;
        foreach (EnemyAI mob in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (mob != this && mob.mobRole != MobRole.None && mob.mobRole != MobRole.SkeletonTactician && !mob.isDead) count++;
        }
        return count;
    }

    // Grants/revokes the Tactical Instruction bonuses (mana regen + chill
    // immunity; the targeting-priority half is read directly in
    // FindTarget) only on an actual state transition, not every tick.
    private void UpdateTacticalInstruction()
    {
        if (mobRole == MobRole.None || mobRole == MobRole.SkeletonTactician) return;

        EnemyAI tactician = FindAliveTactician();
        bool active = tactician != null
            && Vector3.Distance(transform.position, tactician.transform.position) <= tactician.tacticalInstructionRange;

        if (active == tacticalInstructionActive) return;
        tacticalInstructionActive = active;

        float mp5Bonus = mobRole switch
        {
            MobRole.SkeletonHealer => healerMp5Bonus,
            MobRole.SkeletonMage => mageMp5Bonus,
            MobRole.SkeletonArcher => archerMp5Bonus,
            _ => 0f,
        };

        if (active)
        {
            // mp5 -> per-second rate, same conversion as any other
            // ManaRegenRate bonus (paid out in 5s lumps).
            if (mp5Bonus > 0f) stats.ManaRegenRate.AddModifier(new StatModifier(mp5Bonus / 5f, StatModifierType.Flat, this));
            if (chillImmunityEffect != null) stats.AddImmunity(new EffectImmunity { Effect = chillImmunityEffect, GroundOnly = false }, this);
        }
        else
        {
            stats.ManaRegenRate.RemoveAllModifiersFromSource(this);
            stats.RemoveImmunitiesFromSource(this);
        }
    }

    // Shield Bash auto-fires on the current target whenever off cooldown
    // and in melee range. Enrage triggers (once per window, guarded by
    // HasActiveEffect so it doesn't re-heal every tick) when any player is
    // within range of an alive Tactician - not the Warrior's own position.
    private void TickWarriorAbilities(CharacterStats target)
    {
        if (Time.time >= nextStunTime && warriorStunEffect != null)
        {
            float range = mainHandWeapon != null ? mainHandWeapon.Range : WeaponData.BasicAttackRange;
            if (Vector3.Distance(transform.position, target.transform.position) <= range)
            {
                nextStunTime = Time.time + warriorStunCooldown;
                target.ApplyEffect(warriorStunEffect, warriorStunDuration, CharacterStats.NoAttacker, HitSource.Ability);
            }
        }

        if (warriorEnrageEffect == null || stats.HasActiveEffect(warriorEnrageEffect)) return;

        EnemyAI tactician = FindAliveTactician();
        if (tactician == null) return;

        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            CharacterStats playerStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (playerStats == null || playerStats.CurrentHealth.Value <= 0f) continue;
            if (Vector3.Distance(playerStats.transform.position, tactician.transform.position) > warriorEnrageTriggerRange) continue;

            stats.ApplyEffect(warriorEnrageEffect, warriorEnrageDuration, CharacterStats.NoAttacker, HitSource.Ability);
            stats.Heal(warriorEnrageHeal);
            break;
        }
    }

    // Tendon Shot: weapon damage + a slow, skipped if any archer already
    // has one active on this same target. Concussive Shot: this Archer's
    // OWN current target dropping below the threshold.
    private void TickArcherAbilities(CharacterStats target)
    {
        float range = mainHandWeapon != null ? mainHandWeapon.Range : WeaponData.BasicAttackRange;
        bool inRange = Vector3.Distance(transform.position, target.transform.position) <= range;
        if (!inRange) return;

        if (Time.time >= nextTendonShotTime && archerSlowEffect != null
            && !target.HasActiveEffect(archerSlowEffect) && stats.HasEnoughMana(tendonShotCost))
        {
            nextTendonShotTime = Time.time + tendonShotCooldown;
            stats.TrySpendMana(tendonShotCost);
            float weaponDamage = mainHandWeapon != null ? mainHandWeapon.Damage : 0f;
            target.ReceiveHit(new HitInfo
            {
                Damage = weaponDamage,
                AttackerClientId = CharacterStats.NoAttacker,
                Attacker = stats,
                Source = HitSource.Ability,
                Effect = archerSlowEffect,
            });
        }

        if (Time.time >= nextConcussiveShotTime && archerVisionEffect != null
            && target.MaxHealth.Value > 0f && target.CurrentHealth.Value / target.MaxHealth.Value < concussiveShotHpThreshold
            && stats.HasEnoughMana(concussiveShotCost))
        {
            nextConcussiveShotTime = Time.time + concussiveShotCooldown;
            stats.TrySpendMana(concussiveShotCost);
            target.ApplyEffect(archerVisionEffect, archerVisionEffect.Duration, CharacterStats.NoAttacker, HitSource.Ability);
        }
    }

    // Called by the Mage when it casts Enshroud to protect this specific
    // Archer - moves it away, preferring the far side of the nearest
    // Warrior from its current target, without losing its own target range.
    public void TriggerReposition()
    {
        if (!IsServer || mobRole != MobRole.SkeletonArcher) return;
        Vector3 destination = ComputeRepositionTarget();
        if ((destination - transform.position).sqrMagnitude < 0.01f) return;

        float travelTime = Vector3.Distance(transform.position, destination) / Mathf.Max(1f, stats.RunSpeed.Value);
        ServerBeginPull(destination, stats.RunSpeed.Value, travelTime);
    }

    private Vector3 ComputeRepositionTarget()
    {
        if (currentTarget == null) return transform.position;

        EnemyAI warrior = FindNearestAlly(MobRole.SkeletonWarrior);
        Vector3 direction;
        if (warrior != null)
        {
            // "Behind" the Warrior, as seen from the target - further from
            // the target than the Warrior currently is.
            Vector3 fromTargetToWarrior = warrior.transform.position - currentTarget.transform.position;
            fromTargetToWarrior.y = 0f;
            direction = fromTargetToWarrior.sqrMagnitude > 0.01f
                ? fromTargetToWarrior.normalized
                : (transform.position - currentTarget.transform.position).normalized;
        }
        else
        {
            direction = (transform.position - currentTarget.transform.position).normalized;
        }

        Vector3 candidate = transform.position + direction * archerRepositionDistance;

        float range = mainHandWeapon != null ? mainHandWeapon.Range : WeaponData.BasicAttackRange;
        Vector3 fromTarget = candidate - currentTarget.transform.position;
        fromTarget.y = 0f;
        if (fromTarget.magnitude > range)
        {
            candidate = currentTarget.transform.position + fromTarget.normalized * range * 0.9f;
        }
        return candidate;
    }

    // Underworld Guardian (HoT) and Persistence of the Undead (burst heal)
    // both scan every escort member (Tactician included); Mana Leech drains
    // this Healer's own current target once its own mana runs low.
    private void TickHealerAbilities()
    {
        if (healerHotEffect != null && stats.HasEnoughMana(underworldGuardianCost))
        {
            EnemyAI ally = FindAllyBelowThreshold(underworldGuardianHpThreshold, healerHotEffect, underworldGuardianRange);
            if (ally != null)
            {
                stats.TrySpendMana(underworldGuardianCost);
                ally.stats.ApplyEffect(healerHotEffect, healerHotEffect.Duration, CharacterStats.NoAttacker, HitSource.Ability);
            }
        }

        if (!persistenceCasting && stats.HasEnoughMana(persistenceCost))
        {
            EnemyAI ally = FindAllyBelowThreshold(persistenceHpThreshold, null, persistenceRange);
            if (ally != null)
            {
                stats.TrySpendMana(persistenceCost);
                persistenceCasting = true;
                StartCoroutine(CastPersistenceOfTheUndead(ally));
            }
        }

        if (currentTarget != null && stats.CurrentMana.Value < manaLeechManaThreshold && stats.HasEnoughMana(manaLeechCost)
            && Vector3.Distance(transform.position, currentTarget.transform.position) <= manaLeechRange)
        {
            stats.TrySpendMana(manaLeechCost);
            currentTarget.DrainMana(manaLeechAmount);
            stats.RestoreMana(manaLeechAmount);
        }
    }

    private IEnumerator CastPersistenceOfTheUndead(EnemyAI ally)
    {
        yield return new WaitForSeconds(persistenceCastTime);
        persistenceCasting = false;
        if (ally != null && !ally.isDead) ally.stats.Heal(persistenceHeal);
    }

    // Enshroud: a player within range of any Archer ally gets darkened and
    // that specific Archer repositions to safety. Arcane Shield: the lowest
    // escort member (Tactician included) below half HP gets a dome that
    // blocks ranged attacks from reaching them.
    private void TickMageAbilities()
    {
        if (Time.time >= nextEnshroudTime && mageEnshroudEffect != null && stats.HasEnoughMana(enshroudCost))
        {
            foreach (EnemyAI archer in FindAllAllies(MobRole.SkeletonArcher))
            {
                CharacterStats threat = FindNearestAlivePlayer(archer.transform.position, enshroudTriggerRange);
                if (threat == null) continue;

                nextEnshroudTime = Time.time + enshroudCooldown;
                stats.TrySpendMana(enshroudCost);
                threat.ApplyEffect(mageEnshroudEffect, mageEnshroudEffect.Duration, CharacterStats.NoAttacker, HitSource.Ability);
                archer.TriggerReposition();
                break;
            }
        }

        if (Time.time >= nextArcaneShieldTime && stats.HasEnoughMana(arcaneShieldCost))
        {
            EnemyAI ally = FindAllyBelowThreshold(arcaneShieldHpThreshold, null);
            if (ally != null)
            {
                nextArcaneShieldTime = Time.time + arcaneShieldCooldown;
                stats.TrySpendMana(arcaneShieldCost);
                ArcaneShieldZones.Add(ally.transform.position, arcaneShieldRadius, arcaneShieldDuration);
                if (arcaneShieldBuffEffect != null)
                {
                    ally.stats.ApplyEffect(arcaneShieldBuffEffect, arcaneShieldDuration, CharacterStats.NoAttacker, HitSource.Ability);
                }
            }
        }
    }

    // Opens ranged (kiting, protected by the escort) and stays that way
    // until directly engaged by a player (closing to melee engage range),
    // or the escort is mostly dead (fewer than 3 left) - either flips it
    // permanently into melee mode. While still ranged, it tries to hold
    // roughly its own weapon's range rather than closing or fleeing further.
    private void TickTacticianBehavior()
    {
        CharacterStats target = FindTargetByMode(TargetingMode.HighestThreat);
        currentTarget = target;
        if (target == null)
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (!tacticianEngaged && distance <= tacticianMeleeEngageRange) tacticianEngaged = true;
        bool useMelee = tacticianEngaged || CountAliveEscort() < 3;

        WeaponData weapon = useMelee ? tacticianMeleeWeapon : tacticianRangedWeapon;
        if (weapon == null) weapon = tacticianMeleeWeapon != null ? tacticianMeleeWeapon : tacticianRangedWeapon;
        float range = weapon != null ? weapon.Range : WeaponData.BasicAttackRange;
        float cadence = weapon != null ? weapon.SwingInterval : attackInterval;

        Vector3 moveDirection = Vector3.zero;
        if (!useMelee && distance < range * 0.9f)
        {
            moveDirection = -toTarget.normalized; // kite back toward max range
        }
        else if (distance > range)
        {
            moveDirection = toTarget.normalized;
        }

        if (moveDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(moveDirection);
            MoveWithGravity(moveDirection * stats.RunSpeed.Value);
            if (animator != null) animator.SetFloat("speed", 1f);
            return;
        }

        MoveWithGravity(Vector3.zero);
        if (animator != null) animator.SetFloat("speed", 0f);

        if (Time.time >= nextAttackTime && weapon != null)
        {
            nextAttackTime = Time.time + cadence;
            if (animator != null) animator.SetTrigger("attack");
            target.ReceiveHit(new HitInfo
            {
                Damage = weapon.Damage,
                AttackerClientId = CharacterStats.NoAttacker,
                Attacker = stats,
                Source = useMelee ? HitSource.Melee : HitSource.Ability,
                Effect = weapon.Effect,
            });
        }
    }

    private void MoveWithGravity(Vector3 horizontalVelocity)
    {
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.fixedDeltaTime;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.fixedDeltaTime);
    }
}
