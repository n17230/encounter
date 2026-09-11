using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
public class EnemyAI : NetworkBehaviour
{
    // Proximity: ignore threat entirely, always chase the nearest player -
    //   goblins use this (deliberately threat-blind).
    // HighestThreat: standard aggro - most damage dealt, tie-break nearest.
    //   Default for most mobs.
    // LowestThreat: inverse aggro, to confuse players - targets whoever has
    //   contributed LEAST (an untouched player counts as 0), tie-break
    //   nearest.
    // FarthestPlayer: always goes after whichever player is farthest away,
    //   ignoring threat entirely.
    private enum TargetingMode
    {
        Proximity,
        HighestThreat,
        LowestThreat,
        FarthestPlayer
    }

    [SerializeField] private TargetingMode targetingMode = TargetingMode.HighestThreat;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private WeaponData mainHandWeapon;
    [SerializeField] private WeaponData offHandWeapon;
    [SerializeField] private float mainHandDamageModifier = 1f;
    [SerializeField] private float offHandDamageModifier = 0.9f;
    [SerializeField] private float attackInterval = 1.5f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float deathDespawnDelay = 2f;

    private CharacterController controller;
    private CharacterStats stats;
    private ThreatTable threatTable;
    private Animator animator;
    private float verticalVelocity;
    private float nextAttackTime;
    private bool nextAttackIsOffHand;
    private bool isDead;

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
        if (IsServer) stats.OnDeath += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) stats.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        isDead = true;
        if (animator != null) animator.SetTrigger("death");
        StartCoroutine(DespawnAfterDelay());
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(deathDespawnDelay);
        NetworkObject.Despawn();
    }

    private void FixedUpdate()
    {
        if (!IsServer || isDead) return;

        CharacterStats target = FindTarget();
        if (target == null)
        {
            MoveWithGravity(Vector3.zero);
            if (animator != null) animator.SetFloat("speed", 0f);
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance > attackRange)
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
                nextAttackTime = Time.time + attackInterval;
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
                    target.ApplyDamage(weapon.Damage * modifier);
                    if (weapon.Debuff != DebuffType.None)
                    {
                        target.ApplyDebuff(weapon.Debuff, weapon.DebuffMagnitude, weapon.DebuffTickInterval, weapon.DebuffDuration);
                    }
                }
            }
        }
    }

    private CharacterStats FindTarget()
    {
        switch (targetingMode)
        {
            case TargetingMode.HighestThreat:
                return FindByThreat(highest: true);
            case TargetingMode.LowestThreat:
                return FindByThreat(highest: false);
            case TargetingMode.FarthestPlayer:
                return FindByDistance(nearest: false);
            case TargetingMode.Proximity:
            default:
                return FindByDistance(nearest: true);
        }
    }

    // Ranks connected, alive players by threat (every valid player is
    // considered, even ones with no ThreatTable entry yet, which count as 0
    // - that's what lets LowestThreat single out someone who hasn't engaged
    // at all). Ties are broken by nearest distance. Falls back to nearest
    // player entirely if this mob has no ThreatTable.
    private CharacterStats FindByThreat(bool highest)
    {
        if (threatTable == null) return FindByDistance(nearest: true);

        List<CharacterStats> candidates = null;
        float bestThreat = 0f;

        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;

            threatTable.ThreatByClientId.TryGetValue(client.ClientId, out float threat);

            bool better = highest ? threat > bestThreat : threat < bestThreat;
            if (candidates == null || better)
            {
                bestThreat = threat;
                candidates = new List<CharacterStats> { candidateStats };
            }
            else if (threat == bestThreat)
            {
                candidates.Add(candidateStats);
            }
        }

        if (candidates == null || candidates.Count == 0) return FindByDistance(nearest: true);
        if (candidates.Count == 1) return candidates[0];

        return NearestOf(candidates);
    }

    private CharacterStats FindByDistance(bool nearest)
    {
        CharacterStats best = null;
        float bestDistance = nearest ? float.MaxValue : float.MinValue;

        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            CharacterStats candidateStats = client.PlayerObject.GetComponent<CharacterStats>();
            if (candidateStats == null || candidateStats.CurrentHealth.Value <= 0f) continue;

            float distance = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            bool better = nearest ? distance < bestDistance : distance > bestDistance;
            if (best == null || better)
            {
                bestDistance = distance;
                best = candidateStats;
            }
        }

        return best;
    }

    private CharacterStats NearestOf(List<CharacterStats> candidates)
    {
        CharacterStats nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (CharacterStats candidate in candidates)
        {
            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }
        return nearest;
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
