using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(CharacterStats))]
public class PlayerAbilities : NetworkBehaviour
{
    // All abilities that exist in the game. Index here must match the order
    // of MainMenu's "Available Abilities" list, since the client sends the
    // server a pool index (not the AbilityData asset itself - ScriptableObjects
    // can't cross an RPC) to identify which spell a slot is bound to.
    [SerializeField] private List<AbilityData> abilityPool = new List<AbilityData>();
    [SerializeField] private float facingConeAngle = 120f;
    public float FacingConeAngle => facingConeAngle;

    private PlayerTargeting targeting;
    private CharacterStats stats;

    private bool isCasting;
    private float castStartTime;
    private float castDuration;
    private string castingAbilityName;

    private string fizzleMessage;
    private float fizzleMessageEndTime;

    // Server-authoritative: for each of the 8 loadout slots, which abilityPool
    // index (or -1 if empty) the owning player has bound to it.
    private readonly int[] serverSlotPoolIndex = new int[LoadoutSelection.MaxSlots];
    private readonly float[] cooldownReadyTime = new float[64];

    // Server-authoritative cast lock - while Time.time is before this, no
    // new cast (instant or otherwise) can start, regardless of which
    // ability/slot. Client-side isCasting only gates the local UI/input;
    // this is what actually enforces the rule against a desynced or
    // malicious client.
    private float serverCastEndTime;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        stats = GetComponent<CharacterStats>();
        for (int i = 0; i < serverSlotPoolIndex.Length; i++) serverSlotPoolIndex[i] = -1;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        SyncLoadoutToServer();
        MainMenu.Closed += SyncLoadoutToServer;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        MainMenu.Closed -= SyncLoadoutToServer;
    }

    private void SyncLoadoutToServer()
    {
        int[] slotToPoolIndex = new int[LoadoutSelection.MaxSlots];
        for (int i = 0; i < LoadoutSelection.MaxSlots; i++)
        {
            slotToPoolIndex[i] = abilityPool.IndexOf(LoadoutSelection.SlotAbilities[i]);
        }
        SetLoadoutServerRpc(slotToPoolIndex);
    }

    [ServerRpc]
    private void SetLoadoutServerRpc(int[] slotToPoolIndex)
    {
        for (int i = 0; i < serverSlotPoolIndex.Length && i < slotToPoolIndex.Length; i++)
        {
            int poolIndex = slotToPoolIndex[i];
            serverSlotPoolIndex[i] = (poolIndex >= 0 && poolIndex < abilityPool.Count) ? poolIndex : -1;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (MainMenu.IsOpen) return;
        if (isCasting) return; // can't start a new cast until the current one finishes

        for (int slot = 0; slot < LoadoutSelection.MaxSlots; slot++)
        {
            AbilityData ability = LoadoutSelection.SlotAbilities[slot];
            KeyBindingOption? key = LoadoutSelection.SlotKeys[slot];
            if (ability == null || !key.HasValue) continue;
            if (!key.Value.WasPressedThisFrame()) continue;

            Targetable target = targeting.CurrentTarget;
            if (ability.RequiresTarget && target == null) continue;

            NetworkObject targetNetworkObject = target != null ? target.GetComponent<NetworkObject>() : null;
            if (ability.RequiresTarget && targetNetworkObject == null) continue;
            if (ability.RequiresTarget && !IsWithinFacingCone(targetNetworkObject)) continue;

            CastAbilityServerRpc(slot, targetNetworkObject != null ? targetNetworkObject.NetworkObjectId : 0);

            if (ability.CastTime > 0f)
            {
                isCasting = true;
                castStartTime = Time.time;
                castDuration = ability.CastTime;
                castingAbilityName = ability.AbilityName;
            }
        }
    }

    private void OnGUI()
    {
        if (!IsOwner) return;

        UIScale.Apply();
        const float barWidth = 300f;
        const float barHeight = 24f;
        float x = (UIScale.Width - barWidth) * 0.5f;
        float y = UIScale.Height - 80f;

        if (isCasting)
        {
            float elapsed = Time.time - castStartTime;
            if (elapsed >= castDuration)
            {
                isCasting = false;
            }
            else
            {
                float progress = Mathf.Clamp01(elapsed / castDuration);

                GUI.Box(new Rect(x, y, barWidth, barHeight), GUIContent.none);

                Color previousColor = GUI.color;
                GUI.color = Color.cyan;
                GUI.DrawTexture(new Rect(x, y, barWidth * progress, barHeight), Texture2D.whiteTexture);
                GUI.color = previousColor;

                GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(x, y, barWidth, barHeight), castingAbilityName, centered);
            }
        }

        // Fizzle notice fires after the bar is already gone (the failure is
        // only known once the server tries to resolve at the end of the
        // cast), so it's drawn independently, right above the bar's spot.
        if (Time.time < fizzleMessageEndTime)
        {
            GUIStyle fizzleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            fizzleStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(x, y - 22f, barWidth, 20f), fizzleMessage, fizzleStyle);
        }
    }

    [ClientRpc]
    private void NotifyCastFizzledClientRpc(string reason)
    {
        if (!IsOwner) return;
        fizzleMessage = reason;
        fizzleMessageEndTime = Time.time + 2f;
    }

    [ServerRpc]
    private void CastAbilityServerRpc(int slotIndex, ulong targetNetworkObjectId)
    {
        if (Time.time < serverCastEndTime) return; // still resolving a previous cast

        if (slotIndex < 0 || slotIndex >= serverSlotPoolIndex.Length) return;

        int poolIndex = serverSlotPoolIndex[slotIndex];
        if (poolIndex < 0 || poolIndex >= abilityPool.Count) return;

        AbilityData ability = abilityPool[poolIndex];
        if (ability == null) return;
        if (Time.time < cooldownReadyTime[poolIndex]) return;

        if (ability.RequiresTarget)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject startTargetObject)) return;
            if (!IsWithinFacingCone(startTargetObject)) return;
        }

        if (!stats.TrySpendMana(ability.ManaCost)) return;

        cooldownReadyTime[poolIndex] = Time.time + ability.Cooldown;

        if (ability.CastTime > 0f)
        {
            serverCastEndTime = Time.time + ability.CastTime;
            PlayCastVfxClientRpc(poolIndex, ability.CastTime);
            StartCoroutine(ResolveAfterCastTime(ability, targetNetworkObjectId, ability.CastTime));
        }
        else
        {
            ResolveAbility(ability, targetNetworkObjectId);
        }
    }

    // Everyone sees the caster's hand-glow VFX, not just the owner - it's
    // purely cosmetic (no gameplay state), so each client just instantiates
    // it locally rather than it being a NetworkObject. poolIndex is used
    // instead of the AbilityData itself since ScriptableObjects can't cross
    // an RPC, same as everywhere else abilities are referenced over the wire.
    [ClientRpc]
    private void PlayCastVfxClientRpc(int poolIndex, float duration)
    {
        if (poolIndex < 0 || poolIndex >= abilityPool.Count) return;

        AbilityData ability = abilityPool[poolIndex];
        if (ability == null || ability.CastVfxPrefab == null) return;

        Vector3 spawnPosition = transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
        GameObject vfxInstance = Instantiate(ability.CastVfxPrefab, spawnPosition, transform.rotation, transform);
        Destroy(vfxInstance, duration);
    }

    private IEnumerator ResolveAfterCastTime(AbilityData ability, ulong targetNetworkObjectId, float castTime)
    {
        yield return new WaitForSeconds(castTime);
        ResolveAbility(ability, targetNetworkObjectId);
    }

    private void ResolveAbility(AbilityData ability, ulong targetNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
        {
            NotifyCastFizzledClientRpc("Target lost");
            return;
        }

        Targetable target = targetObject.GetComponent<Targetable>();
        if (target == null || target.Stats == null)
        {
            NotifyCastFizzledClientRpc("Invalid target");
            return;
        }

        float distance = Vector3.Distance(transform.position, targetObject.transform.position);
        if (distance > ability.Range)
        {
            NotifyCastFizzledClientRpc("Target out of range");
            return;
        }
        if (!IsWithinFacingCone(targetObject))
        {
            NotifyCastFizzledClientRpc("Target out of facing cone");
            return;
        }
        if (!HasLineOfSight(targetObject))
        {
            NotifyCastFizzledClientRpc("Line of sight blocked");
            return;
        }

        if (ability.ProjectilePrefab != null)
        {
            Vector3 spawnPosition = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
            GameObject projectileInstance = Instantiate(ability.ProjectilePrefab, spawnPosition, Quaternion.identity);
            projectileInstance.GetComponent<NetworkObject>().Spawn();
            projectileInstance.GetComponent<Projectile>().Initialize(targetNetworkObjectId, ability, OwnerClientId);
        }
        else
        {
            target.Stats.ApplyDamage(ability.Damage, OwnerClientId);
            if (ability.ThreatValue > 0f) target.Stats.AddThreat(ability.ThreatValue, OwnerClientId);
        }
    }

    private bool IsWithinFacingCone(NetworkObject targetObject)
    {
        Vector3 toTarget = targetObject.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return true;

        Vector3 facing = transform.forward;
        facing.y = 0f;
        facing.Normalize();

        float angle = Vector3.Angle(facing, toTarget.normalized);
        return angle <= facingConeAngle * 0.5f;
    }

    private bool HasLineOfSight(NetworkObject targetObject)
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 destination = targetObject.transform.position + Vector3.up * 1.5f;

        if (!Physics.Linecast(origin, destination, out RaycastHit hit)) return true;

        // Other creatures never block line of sight, only real environment
        // geometry does - anything with a Targetable (player or mob) is a
        // creature, regardless of whether it's the caster/target or some
        // unrelated mob standing in the way. Stops casts fizzling just
        // because mobs happened to stack up between you and your target.
        return hit.collider.GetComponentInParent<Targetable>() != null;
    }
}
