using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(PlayerTargeting))]
public class PlayerHUD : NetworkBehaviour
{
    // Mob reveal is an echolocation-style pulse, not a live tracker: every
    // MobPingInterval seconds it snapshots every mob's position, the dots
    // sit frozen where they were captured, and fade out over
    // MobPingFadeDuration (leaving a blind gap before the next pulse).
    private const float MobPingInterval = 5f;
    private const float MobPingFadeDuration = 4f;

    private CharacterStats stats;
    private PlayerTargeting targeting;
    private PlayerAutoAttack autoAttack;
    private CharacterEquipment equipment;

    // The live Targetable registry - no per-frame scene scan needed.
    private static IReadOnlyList<Targetable> minimapBlips => Targetable.All;
    private MinimapReveal minimapReveals;
    private readonly List<Vector3> mobPingPositions = new List<Vector3>();
    private float nextMobPing;
    private float lastMobPingTime = float.NegativeInfinity;
    private float mobPingAlpha;

    // Perception's orbs - owner-local, driven from the local profile's
    // loadout (not CharacterEquipment's synced aura state) since nobody
    // else ever needs to know this player has them.
    private readonly PerceptionOrbs perceptionOrbs = new PerceptionOrbs();

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        targeting = GetComponent<PlayerTargeting>();
        autoAttack = GetComponent<PlayerAutoAttack>();
        equipment = GetComponent<CharacterEquipment>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        minimapReveals = MinimapReveal.None;
        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            ItemData item = ProfileStore.Current.GetEquipment(slot);
            if (item != null) minimapReveals |= item.Reveals;
        }
        if (equipment.CastAuraRevealsMobs.Value) minimapReveals |= MinimapReveal.Mobs;

        UpdateMobPings();

        bool showPerceptionOrbs = false;
        for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
        {
            AbilityData ability = ProfileStore.Current.GetSlotAbility(i);
            showPerceptionOrbs |= ability != null && ability.AuraShowsPerceptionOrbs;
        }
        perceptionOrbs.Update(showPerceptionOrbs);
    }

    // The orbs are plain unparented scene objects, so they'd outlive this
    // player (disconnect, re-host) unless torn down with it.
    public override void OnNetworkDespawn()
    {
        perceptionOrbs.Clear();
    }

    private void UpdateMobPings()
    {
        if ((minimapReveals & MinimapReveal.Mobs) == 0)
        {
            // Reset so re-equipping starts a fresh cycle rather than resuming
            // a stale timer.
            nextMobPing = 0f;
            mobPingPositions.Clear();
            mobPingAlpha = 0f;
            return;
        }

        if (Time.time >= nextMobPing)
        {
            nextMobPing = Time.time + MobPingInterval;
            lastMobPingTime = Time.time;
            mobPingPositions.Clear();
            foreach (Targetable blip in minimapBlips)
            {
                if (blip == null || blip.transform == transform) continue;
                if (blip.GetComponent<PlayerMovement>() != null) continue; // mobs only
                mobPingPositions.Add(blip.transform.position);
            }
        }

        float elapsed = Time.time - lastMobPingTime;
        mobPingAlpha = elapsed < MobPingFadeDuration ? 1f - elapsed / MobPingFadeDuration : 0f;
    }

    private void OnGUI()
    {
        if (!IsOwner) return;

        DevGui.Begin();
        Minimap.Draw(transform, targeting.CurrentTarget, minimapBlips, minimapReveals, mobPingPositions, mobPingAlpha);
        PartyFrames.Draw(minimapBlips, OwnerClientId);
        DrawBar(10, UIScale.Height - 50, 200, 20, stats.CurrentHealth.Value, stats.SyncedMaxHealth.Value, Color.red);
        DrawBar(10, UIScale.Height - 25, 200, 20, stats.CurrentMana.Value, stats.SyncedMaxMana.Value, Color.blue);
        DrawShieldLabel(stats, 10, UIScale.Height - 104);
        DrawEffects(stats, 10, UIScale.Height - 84);

        DrawTargetFrame();

        // The one thing a dead player's own screen says about it (see
        // CharacterStats.IsAlive) - everything else draws as usual. Gated
        // on SyncedMaxHealth: health defaults to 0 until the server's first
        // sync lands, which is a spawn in flight, not a death.
        if (stats.SyncedMaxHealth.Value > 0f && !stats.IsAlive) DrawDeadLabel();
    }

    private static void DrawDeadLabel()
    {
        GUIStyle deadStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 28,
            fontStyle = FontStyle.Bold,
        };
        deadStyle.normal.textColor = Color.red;
        GUI.Label(new Rect(0f, UIScale.Height * 0.5f - 20f, UIScale.Width, 40f), "You are dead", deadStyle);
    }

    private void DrawTargetFrame()
    {
        Targetable target = targeting.CurrentTarget;

        Rect nameRect = new Rect(10, 10, 200, 20);
        // A targeted player shows their lobby name; a mob (or a nameless
        // player) keeps the Targetable's own label - see PlayerIdentity.
        string targetName = target != null ? PlayerIdentity.NameOf(target) ?? target.DisplayName : "No target";
        GUI.Box(nameRect, targetName);

        if (target != null && target.Stats != null)
        {
            DrawBar(10, 32, 200, 16, target.Stats.CurrentHealth.Value, target.Stats.SyncedMaxHealth.Value, Color.red);
            DrawShieldLabel(target.Stats, 10, 50);
            DrawEffects(target.Stats, 10, 68);
        }

        if (autoAttack != null && autoAttack.IsArmed)
        {
            WeaponData weapon = autoAttack.LocalWeapon;
            GUI.Label(new Rect(10, 98, 300, 20), $"Auto-attacking ({(weapon != null ? weapon.WeaponName : "unarmed")})");
        }
    }

    private static void DrawShieldLabel(CharacterStats subject, float x, float y)
    {
        if (subject.ShieldAmount.Value <= 0f) return;
        GUIStyle shieldStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.cyan } };
        GUI.Label(new Rect(x, y, 200, 18), $"Shield: {subject.ShieldAmount.Value:0}", shieldStyle);
    }

    // Effects with their own Icon set (currently just the two aura-pulsed
    // effects, EffectRejuvenation/EffectManaAura) render as a small icon
    // instead of "Name Xs" text - a live countdown is meaningless noise
    // for something that's continuously re-refreshed by the aura. Icons
    // draw first, left to right, then the remaining text-based effects
    // share one label starting after them.
    private void DrawEffects(CharacterStats subject, float x, float y)
    {
        if (subject.ActiveEffects.Count == 0) return;

        const float auraIconSize = 28f; // 25% of the 56px ability bar slot's AREA (0.5 linear, since area scales with the square)
        double now = NetworkManager.ServerTime.Time;
        float iconX = x;
        StringBuilder sb = new StringBuilder();

        foreach (ActiveEffectNet entry in subject.ActiveEffects)
        {
            StatusEffectData effect = GameDatabase.GetEffect(entry.EffectId.ToString());

            if (effect != null && effect.Icon != null)
            {
                GUI.DrawTexture(new Rect(iconX, y, auraIconSize, auraIconSize), effect.Icon.texture);
                iconX += auraIconSize + 2f;
                continue;
            }

            string name = effect != null ? effect.DisplayName : entry.EffectId.ToString();
            double remaining = System.Math.Max(0.0, entry.ExpireServerTime - now);
            if (sb.Length > 0) sb.Append("   ");
            sb.Append($"{name} {remaining:0.0}s");
        }

        if (sb.Length > 0) GUI.Label(new Rect(iconX, y, 400f, 20f), sb.ToString());
    }

    private void DrawBar(float x, float y, float width, float height, float current, float max, Color fillColor)
    {
        Rect background = new Rect(x, y, width, height);
        GUI.Box(background, GUIContent.none);

        float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        Rect fill = new Rect(x, y, width * pct, height);
        Color previousColor = GUI.color;
        GUI.color = fillColor;
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = previousColor;

        GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(background, $"{current:0}/{max:0}", centered);
    }
}
