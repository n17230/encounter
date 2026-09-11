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

    // See PlayerMovement.ServerBeginPull.
    public void ServerBeginPull(Vector3 towardPosition, float speed, float duration)
    {
        if (!IsServer) return;
        pullTargetPosition = towardPosition;
        pullSpeed = speed;
        pullEndTime = Time.time + duration;
    }

    private void FixedUpdate()
    {
        if (!IsServer || isDead) return;

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

        // Cadence comes from the main-hand weapon when there is one; the
        // serialized interval is the fallback for unarmed mobs.
        float cadence = mainHandWeapon != null ? mainHandWeapon.SwingInterval : attackInterval;

        if (distance > WeaponData.BasicAttackRange)
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
                        Source = HitSource.Melee,
                        Effect = weapon.Effect,
                    });
                }
            }
        }
    }

    // Every connected, alive player is a candidate - including ones with no
    // threat entry yet (counted as 0), which is what lets LowestThreat single
    // out someone who hasn't engaged at all.
    private CharacterStats FindTarget()
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

        return TargetSelector.Select(candidates, targetingMode, threatTable != null);
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
