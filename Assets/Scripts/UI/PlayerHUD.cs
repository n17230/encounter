using System.Text;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(PlayerTargeting))]
public class PlayerHUD : NetworkBehaviour
{
    private CharacterStats stats;
    private PlayerTargeting targeting;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        targeting = GetComponent<PlayerTargeting>();
    }

    private void OnGUI()
    {
        if (!IsOwner) return;

        UIScale.Apply();
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
