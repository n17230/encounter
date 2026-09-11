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

    // Refreshed once per frame in Update rather than per OnGUI pass.
    private Targetable[] minimapBlips = new Targetable[0];
    private MinimapReveal minimapReveals;
    private readonly List<Vector3> mobPingPositions = new List<Vector3>();
    private float nextMobPing;
    private float lastMobPingTime = float.NegativeInfinity;
    private float mobPingAlpha;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        targeting = GetComponent<PlayerTargeting>();
        autoAttack = GetComponent<PlayerAutoAttack>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        minimapReveals = MinimapReveal.None;
        foreach (GearSlot slot in System.Enum.GetValues(typeof(GearSlot)))
        {
            ItemData item = ProfileStore.Current.GetGear(slot);
            if (item != null) minimapReveals |= item.Reveals;
        }

        // Always gathered: a broadcasting ally draws even with no reveals.
        minimapBlips = FindObjectsByType<Targetable>(FindObjectsSortMode.None);

        UpdateMobPings();
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
        GUI.Label(new Rect(10, UIScale.Height - 72, 400, 20), DescribeEffects(stats));

        DrawTargetFrame();
    }

    private void DrawTargetFrame()
    {
        Targetable target = targeting.CurrentTarget;

        Rect nameRect = new Rect(10, 10, 200, 20);
        GUI.Box(nameRect, target != null ? target.DisplayName : "No target");

        if (target != null && target.Stats != null)
        {
            DrawBar(10, 32, 200, 16, target.Stats.CurrentHealth.Value, target.Stats.SyncedMaxHealth.Value, Color.red);
            GUI.Label(new Rect(10, 50, 400, 20), DescribeEffects(target.Stats));
        }

        if (autoAttack != null && autoAttack.IsArmed)
        {
            WeaponData weapon = autoAttack.LocalWeapon;
            GUI.Label(new Rect(10, 68, 300, 20), $"Auto-attacking ({(weapon != null ? weapon.WeaponName : "unarmed")})");
        }
    }

    private string DescribeEffects(CharacterStats subject)
    {
        if (subject.ActiveEffects.Count == 0) return "";

        double now = NetworkManager.ServerTime.Time;
        StringBuilder sb = new StringBuilder();
        foreach (ActiveEffectNet entry in subject.ActiveEffects)
        {
            StatusEffectData effect = GameDatabase.GetEffect(entry.EffectId.ToString());
            string name = effect != null ? effect.DisplayName : entry.EffectId.ToString();
            double remaining = System.Math.Max(0.0, entry.ExpireServerTime - now);
            if (sb.Length > 0) sb.Append("   ");
            sb.Append($"{name} {remaining:0.0}s");
        }
        return sb.ToString();
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
