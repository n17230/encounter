using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(PlayerCamera))]
[RequireComponent(typeof(CharacterStats))]
public class PlayerAbilities : NetworkBehaviour
{
    [SerializeField] private float facingConeAngle = 120f;
    [SerializeField] private Color groundReticleColor = new Color(0.4f, 0.8f, 1f, 0.6f);
    public float FacingConeAngle => facingConeAngle;

    private PlayerTargeting targeting;
    private PlayerCamera playerCameraComponent;
    private CharacterStats stats;

    // Owner-local aiming state for a ground-targeted ability (see
    // AbilityData.IsGroundTargeted). Exposed so PlayerTargeting can ignore
    // clicks meant for placement, not targeting.
    public bool IsAimingGroundTarget { get; private set; }
    private int aimingSlot = -1;
    private AbilityData aimingAbility;
    private GroundTargetReticle reticle;
    private Vector3? aimedGroundPoint;

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

    // Server-authoritative: for an AbilityData.ExclusiveSingleTarget
    // ability, who currently holds the effect from THIS caster's casts of
    // it (see ResolveAbility). Per-caster, not global.
    private readonly Dictionary<AbilityData, Targetable> exclusiveTargets = new Dictionary<AbilityData, Targetable>();

    // Server-authoritative cast lock - while Time.time is before this, no
    // new cast (instant or otherwise) can start, regardless of which
    // ability/slot. Client-side isCasting only gates the local UI/input;
    // this is what actually enforces the rule against a desynced or
    // malicious client.
    private float serverCastEndTime;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        playerCameraComponent = GetComponent<PlayerCamera>();
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
        reticle?.Destroy();
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

        if (MainMenu.IsOpen)
        {
            // Opening the menu (whatever triggered it) abandons any in-progress
            // aim rather than leaving a stuck reticle up behind it.
            if (IsAimingGroundTarget) CancelGroundTargeting();
            return;
        }

        if (IsAimingGroundTarget)
        {
            UpdateGroundAiming();
            if (Input.GetMouseButtonDown(0))
            {
                ConfirmGroundTarget();
                return;
            }
        }

        if (isCasting) return; // can't start a new cast until the current one finishes

        PlayerProfile profile = ProfileStore.Current;
        for (int slot = 0; slot < PlayerProfile.AbilitySlots; slot++)
        {
            AbilityData ability = profile.GetSlotAbility(slot);
            KeyBindingOption? key = profile.GetSlotKey(slot);
            if (ability == null || !key.HasValue) continue;
            if (!key.Value.WasPressedThisFrame()) continue;

            if (ability.IsGroundTargeted)
            {
                if (IsAimingGroundTarget)
                {
                    // Only the aiming ability's own key does anything (cancels);
                    // other hotkeys are inert while aiming.
                    if (aimingSlot == slot) CancelGroundTargeting();
                    continue;
                }

                string groundRejection = ClientPrecheck(ability, out _);
                if (groundRejection != null)
                {
                    ShowNotice(groundRejection);
                    continue;
                }

                BeginGroundTargeting(slot, ability);
                continue;
            }

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

    private void BeginGroundTargeting(int slot, AbilityData ability)
    {
        IsAimingGroundTarget = true;
        aimingSlot = slot;
        aimingAbility = ability;
        aimedGroundPoint = null;

        reticle?.Destroy();
        reticle = new GroundTargetReticle(ability.GroundEffectRadius, groundReticleColor);
    }

    private void CancelGroundTargeting()
    {
        IsAimingGroundTarget = false;
        aimingSlot = -1;
        aimingAbility = null;
        aimedGroundPoint = null;
        reticle?.Destroy();
        reticle = null;
    }

    // Raycasts the mouse against the world (ignoring characters, so you can
    // aim through a mob standing in front of the spot) and moves the
    // reticle there; no hit just hides it until the mouse finds ground.
    private void UpdateGroundAiming()
    {
        Camera cam = playerCameraComponent.Camera;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        int ignoreCharacters = ~LayerMask.GetMask("Characters");
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, ignoreCharacters))
        {
            aimedGroundPoint = hit.point;
            reticle.SetPosition(hit.point);
            reticle.SetVisible(true);
        }
        else
        {
            aimedGroundPoint = null;
            reticle.SetVisible(false);
        }
    }

    private void ConfirmGroundTarget()
    {
        if (!aimedGroundPoint.HasValue) return; // nothing under the cursor yet - stay aiming

        Vector3 point = aimedGroundPoint.Value;
        if (Vector3.Distance(transform.position, point) > aimingAbility.Range)
        {
            ShowNotice("Out of range");
            return; // stay aiming - move closer and click again
        }

        AbilityData ability = aimingAbility;
        int slot = aimingSlot;
        CancelGroundTargeting();

        CastGroundTargetedAbilityServerRpc(slot, point);
        predictedCooldownReady[ability] = Time.time + ability.Cooldown;

        if (ability.CastTime > 0f)
        {
            isCasting = true;
            castStartTime = Time.time;
            castDuration = ability.CastTime;
            castingAbilityName = ability.AbilityName;
        }
    }

    // Mirrors the server's start-of-cast checks using replicated state, so
    // the common failures are reported instantly without a round trip.
    private string ClientPrecheck(AbilityData ability, out NetworkObject targetNetworkObject)
    {
        targetNetworkObject = null;

        if (predictedCooldownReady.TryGetValue(ability, out float ready) && Time.time < ready) return "Not ready";
        if (stats.CurrentMana.Value < ability.ManaCost * stats.SyncedManaCostMultiplier.Value) return "Not enough mana";

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

        if (!stats.HasEnoughMana(ability.ManaCost))
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not enough mana");
            return;
        }

        // Mana is only actually spent once the cast succeeds - see
        // ResolveAbility - not here at cast start. This check just stops an
        // unaffordable cast from starting in the first place.
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
        if (ability.AreaAroundCaster)
        {
            ResolveAreaAroundCaster(ability);
            return;
        }

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

        // Every fizzle check above has passed - the cast is actually
        // succeeding, so this is where mana is spent (not at cast start;
        // a fizzled cast costs nothing).
        if (!stats.TrySpendMana(ability.ManaCost))
        {
            NotifyCastFizzledClientRpc("Not enough mana");
            return;
        }

        if (ability.RecallTarget)
        {
            Vector3 recallPosition = transform.position;
            if (targetObject.TryGetComponent(out PlayerMovement targetMovement))
            {
                targetMovement.ServerTeleportTo(recallPosition);
            }
            else if (targetObject.TryGetComponent(out EnemyAI targetEnemyAi))
            {
                targetEnemyAi.ServerTeleportTo(recallPosition);
            }
        }
        else if (ability.ProjectilePrefab != null)
        {
            Vector3 spawnPosition = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
            GameObject projectileInstance = Instantiate(ability.ProjectilePrefab, spawnPosition, Quaternion.identity);
            projectileInstance.GetComponent<NetworkObject>().Spawn();
            projectileInstance.GetComponent<Projectile>().Initialize(targetNetworkObjectId, ability, OwnerClientId);
        }
        else
        {
            if (ability.ExclusiveSingleTarget && ability.Effect != null)
            {
                // Strip it from whoever held it before, if that's someone
                // else - recasting on the same person just refreshes below.
                if (exclusiveTargets.TryGetValue(ability, out Targetable previous)
                    && previous != null && previous != target)
                {
                    previous.Stats?.RemoveEffect(ability.Effect);
                }
                exclusiveTargets[ability] = target;
            }

            target.Stats.ReceiveHit(new HitInfo
            {
                Damage = ability.Damage,
                Heal = ability.HealAmount,
                ShieldAmount = ability.ShieldAmount,
                ExtraThreat = ability.ThreatValue,
                AttackerClientId = OwnerClientId,
                Source = HitSource.Ability,
                Effect = ability.Effect,
                EffectDuration = ability.DirectHitEffectDuration,
            });
        }
    }

    // No unit/ground targeting at all - resolves centered on the caster's
    // own current position, affecting every player (caster included)
    // within GroundEffectRadius. Mana is spent here (this ability's
    // "successful cast" point), not at cast start, same as the other
    // resolve paths.
    private void ResolveAreaAroundCaster(AbilityData ability)
    {
        if (!stats.TrySpendMana(ability.ManaCost))
        {
            NotifyCastFizzledClientRpc("Not enough mana");
            return;
        }

        foreach (Targetable candidate in FindObjectsByType<Targetable>(FindObjectsSortMode.None))
        {
            if (candidate == null) continue;
            if (candidate.GetComponent<PlayerMovement>() == null) continue; // allies only, not mobs
            if (candidate.Stats == null || candidate.Stats.CurrentHealth.Value <= 0f) continue;
            if (Vector3.Distance(transform.position, candidate.transform.position) > ability.GroundEffectRadius) continue;

            candidate.Stats.ReceiveHit(new HitInfo
            {
                Heal = ability.HealAmount,
                ShieldAmount = ability.ShieldAmount,
                AttackerClientId = OwnerClientId,
                Source = HitSource.Ability,
                Effect = ability.Effect,
                EffectDuration = ability.DirectHitEffectDuration,
            });
        }
    }

    [ServerRpc]
    private void CastGroundTargetedAbilityServerRpc(int slotIndex, Vector3 groundPosition)
    {
        if (slotIndex < 0 || slotIndex >= serverSlotAbilities.Length) return;

        AbilityData ability = serverSlotAbilities[slotIndex];
        if (ability == null || !ability.IsGroundTargeted) return;

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
        if (Vector3.Distance(transform.position, groundPosition) > ability.Range)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Out of range");
            return;
        }
        if (!stats.HasEnoughMana(ability.ManaCost))
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not enough mana");
            return;
        }

        // Spent at resolve, not here - see ResolveGroundAbility.
        cooldownReadyTime[ability] = Time.time + ability.Cooldown;

        if (ability.CastTime > 0f)
        {
            serverCastEndTime = Time.time + ability.CastTime;
            PlayCastVfxClientRpc(ability.Id, ability.CastTime);
            StartCoroutine(ResolveGroundAbilityAfterCastTime(ability, groundPosition, ability.CastTime));
        }
        else
        {
            ResolveGroundAbility(ability, groundPosition);
        }
    }

    private IEnumerator ResolveGroundAbilityAfterCastTime(AbilityData ability, Vector3 groundPosition, float castTime)
    {
        yield return new WaitForSeconds(castTime);
        ResolveGroundAbility(ability, groundPosition);
    }

    // No range/facing/LoS re-check at resolve time (unlike unit-targeted
    // abilities) - the point was already fixed and validated at cast start,
    // and a ground AoE has no single thing to lose sight of. Mana is still
    // spent here rather than at cast start, for consistency with
    // ResolveAbility (and so a class of failure this method might grow
    // later - e.g. a re-check - doesn't cost mana on fizzle).
    private void ResolveGroundAbility(AbilityData ability, Vector3 groundPosition)
    {
        if (!stats.TrySpendMana(ability.ManaCost))
        {
            NotifyCastFizzledClientRpc("Not enough mana");
            return;
        }

        if (ability.ForceSpeed <= 0f || ability.GroundEffectRadius <= 0f) return;

        // A push is implemented as "pull toward a point on the far side of
        // you" - ServerBeginPull only ever drives a character straight at
        // whatever point it's given, so the two directions share every line
        // of the actual movement code (including the movement-validator
        // suppression, which is the part worth not duplicating).
        const float pushClearance = 8f; // how far past the radius a push sends its victims
        float pushDistance = ability.GroundEffectRadius + pushClearance;

        foreach (Targetable candidate in FindObjectsByType<Targetable>(FindObjectsSortMode.None))
        {
            if (candidate == null) continue;
            if (candidate.Stats != null && candidate.Stats.CurrentHealth.Value <= 0f) continue;

            Vector3 offset = candidate.transform.position - groundPosition;
            offset.y = 0f;
            if (offset.magnitude > ability.GroundEffectRadius) continue;

            Vector3 forceTarget;
            if (ability.PushAway)
            {
                Vector3 radial = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.forward;
                forceTarget = groundPosition + radial * pushDistance;
            }
            else
            {
                forceTarget = groundPosition;
            }

            // Generous cap in case something never quite arrives (e.g.
            // blocked by terrain) - it simply regains control then.
            float duration = Vector3.Distance(candidate.transform.position, forceTarget) / ability.ForceSpeed + 0.5f;

            if (candidate.TryGetComponent(out PlayerMovement playerMovement))
            {
                playerMovement.ServerBeginPull(forceTarget, ability.ForceSpeed, duration);
            }
            else if (candidate.TryGetComponent(out EnemyAI enemyAi))
            {
                enemyAi.ServerBeginPull(forceTarget, ability.ForceSpeed, duration);
            }
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
