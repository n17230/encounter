using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(CharacterStats))]
public class PlayerAbilities : NetworkBehaviour
{
    [SerializeField] private float facingConeAngle = 120f;
    public float FacingConeAngle => facingConeAngle;

    private PlayerTargeting targeting;
    private CharacterStats stats;

    private bool isCasting;
    private float castStartTime;
    private float castDuration;
    private string castingAbilityName;

    private string noticeMessage;
    private float noticeEndTime;

    // Owner-side prediction, MMO style: the client pre-checks what it can
    // (target, range, facing, mana, cooldown) and starts the cooldown the
    // instant the key is pressed, so casting feels immediate. The server
    // still decides; a rejection rolls the predicted cooldown back.
    private readonly Dictionary<AbilityData, float> predictedCooldownReady = new Dictionary<AbilityData, float>();

    // Server-authoritative: which ability (or null) the owning player has in
    // each loadout slot. Clients only ever send slot indices to cast, and
    // ability Ids to (re)assign slots - never the assets themselves.
    private readonly AbilityData[] serverSlotAbilities = new AbilityData[PlayerProfile.AbilitySlots];
    private readonly Dictionary<AbilityData, float> cooldownReadyTime = new Dictionary<AbilityData, float>();

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
        SetLoadoutServerRpc(string.Join(";", ProfileStore.Current.SlotAbilityIds));
    }

    [ServerRpc]
    private void SetLoadoutServerRpc(string joinedAbilityIds)
    {
        string[] ids = (joinedAbilityIds ?? "").Split(';');
        for (int i = 0; i < serverSlotAbilities.Length; i++)
        {
            serverSlotAbilities[i] = i < ids.Length ? GameDatabase.GetAbility(ids[i]) : null;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (MainMenu.IsOpen) return;
        if (isCasting) return; // can't start a new cast until the current one finishes

        PlayerProfile profile = ProfileStore.Current;
        for (int slot = 0; slot < PlayerProfile.AbilitySlots; slot++)
        {
            AbilityData ability = profile.GetSlotAbility(slot);
            KeyBindingOption? key = profile.GetSlotKey(slot);
            if (ability == null || !key.HasValue) continue;
            if (!key.Value.WasPressedThisFrame()) continue;

            string rejection = ClientPrecheck(ability, out NetworkObject targetNetworkObject);
            if (rejection != null)
            {
                ShowNotice(rejection);
                continue;
            }

            CastAbilityServerRpc(slot, targetNetworkObject != null ? targetNetworkObject.NetworkObjectId : 0);
            predictedCooldownReady[ability] = Time.time + ability.Cooldown;

            if (ability.CastTime > 0f)
            {
                isCasting = true;
                castStartTime = Time.time;
                castDuration = ability.CastTime;
                castingAbilityName = ability.AbilityName;
            }
        }
    }

    // Mirrors the server's start-of-cast checks using replicated state, so
    // the common failures are reported instantly without a round trip.
    private string ClientPrecheck(AbilityData ability, out NetworkObject targetNetworkObject)
    {
        targetNetworkObject = null;

        if (predictedCooldownReady.TryGetValue(ability, out float ready) && Time.time < ready) return "Not ready";
        if (stats.CurrentMana.Value < ability.ManaCost) return "Not enough mana";

        if (!ability.RequiresTarget) return null;

        Targetable target = targeting.CurrentTarget;
        if (target == null) return "No target";
        targetNetworkObject = target.GetComponent<NetworkObject>();
        if (targetNetworkObject == null) return "Invalid target";
        if (Vector3.Distance(transform.position, target.transform.position) > ability.Range) return "Out of range";
        if (!IsWithinFacingCone(targetNetworkObject)) return "Target not in front of you";
        return null;
    }

    private void ShowNotice(string message)
    {
        noticeMessage = message;
        noticeEndTime = Time.time + 2f;
    }

    private float PredictedCooldownRemaining(AbilityData ability)
    {
        return predictedCooldownReady.TryGetValue(ability, out float ready) ? Mathf.Max(0f, ready - Time.time) : 0f;
    }

    private void OnGUI()
    {
        if (!IsOwner) return;

        DevGui.Begin();
        const float barWidth = 300f;
        const float barHeight = 24f;
        float x = (UIScale.Width - barWidth) * 0.5f;
        float y = UIScale.Height - 80f;

        DrawAbilityBar(UIScale.Height - 56f);

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

        // Notices (local pre-check failures, server rejections, and fizzles
        // that are only known once a cast resolves) share one spot above
        // the cast bar.
        if (Time.time < noticeEndTime)
        {
            GUIStyle noticeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            noticeStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(x, y - 22f, barWidth, 20f), noticeMessage, noticeStyle);
        }
    }

    // Bottom-centre row of the 8 loadout slots: key, name, and a cooldown
    // sweep driven by the predicted cooldowns (so it starts on key press).
    private void DrawAbilityBar(float bottomY)
    {
        const float slotSize = 56f;
        const float gap = 4f;
        int slotCount = PlayerProfile.AbilitySlots;
        float totalWidth = slotCount * slotSize + (slotCount - 1) * gap;
        float x0 = (UIScale.Width - totalWidth) * 0.5f;
        float y0 = bottomY - slotSize;

        GUIStyle small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, wordWrap = true };
        GUIStyle timer = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
        PlayerProfile profile = ProfileStore.Current;

        for (int i = 0; i < slotCount; i++)
        {
            Rect rect = new Rect(x0 + i * (slotSize + gap), y0, slotSize, slotSize);
            GUI.Box(rect, GUIContent.none);

            AbilityData ability = profile.GetSlotAbility(i);
            if (ability == null) continue;

            GUI.Label(new Rect(rect.x, rect.y + 4f, rect.width, 28f), ability.AbilityName, small);
            KeyBindingOption? key = profile.GetSlotKey(i);
            GUI.Label(new Rect(rect.x, rect.yMax - 16f, rect.width, 14f), key.HasValue ? key.Value.DisplayName : "-", small);

            float remaining = PredictedCooldownRemaining(ability);
            if (remaining <= 0f) continue;

            float fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, ability.Cooldown));
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - rect.height * fraction, rect.width, rect.height * fraction), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(rect, remaining.ToString("0.0"), timer);
        }
    }

    [ClientRpc]
    private void NotifyCastFizzledClientRpc(string reason)
    {
        if (!IsOwner) return;
        ShowNotice(reason);
    }

    // Start-of-cast rejection: undo the optimistic cooldown and cast bar.
    [ClientRpc]
    private void NotifyCastRejectedClientRpc(string abilityId, string reason)
    {
        if (!IsOwner) return;
        AbilityData ability = GameDatabase.GetAbility(abilityId);
        if (ability != null) predictedCooldownReady.Remove(ability);
        if (isCasting && castingAbilityName == ability?.AbilityName) isCasting = false;
        ShowNotice(reason);
    }

    [ServerRpc]
    private void CastAbilityServerRpc(int slotIndex, ulong targetNetworkObjectId)
    {
        if (slotIndex < 0 || slotIndex >= serverSlotAbilities.Length) return;

        AbilityData ability = serverSlotAbilities[slotIndex];
        if (ability == null) return;

        if (Time.time < serverCastEndTime)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Already casting");
            return;
        }
        if (cooldownReadyTime.TryGetValue(ability, out float readyTime) && Time.time < readyTime)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not ready");
            return;
        }

        if (ability.RequiresTarget)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject startTargetObject))
            {
                NotifyCastRejectedClientRpc(ability.Id, "Target lost");
                return;
            }
            if (!IsWithinFacingCone(startTargetObject))
            {
                NotifyCastRejectedClientRpc(ability.Id, "Target not in front of you");
                return;
            }
        }

        if (!stats.TrySpendMana(ability.ManaCost))
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not enough mana");
            return;
        }

        cooldownReadyTime[ability] = Time.time + ability.Cooldown;

        if (ability.CastTime > 0f)
        {
            serverCastEndTime = Time.time + ability.CastTime;
            PlayCastVfxClientRpc(ability.Id, ability.CastTime);
            StartCoroutine(ResolveAfterCastTime(ability, targetNetworkObjectId, ability.CastTime));
        }
        else
        {
            ResolveAbility(ability, targetNetworkObjectId);
        }
    }

    // Everyone sees the caster's hand-glow VFX, not just the owner - it's
    // purely cosmetic (no gameplay state), so each client just instantiates
    // it locally rather than it being a NetworkObject.
    [ClientRpc]
    private void PlayCastVfxClientRpc(string abilityId, float duration)
    {
        AbilityData ability = GameDatabase.GetAbility(abilityId);
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
            target.Stats.ReceiveHit(new HitInfo
            {
                Damage = ability.Damage,
                ExtraThreat = ability.ThreatValue,
                AttackerClientId = OwnerClientId,
                Effect = ability.Effect,
                EffectDuration = ability.DirectHitEffectDuration,
            });
        }
    }

    private bool IsWithinFacingCone(NetworkObject targetObject)
    {
        return FacingCone.IsWithin(transform, targetObject.transform.position, facingConeAngle);
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
