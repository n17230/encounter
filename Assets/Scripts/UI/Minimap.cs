using System.Collections.Generic;
using UnityEngine;

// Circular radar: the local player at the centre, north up, everything
// with a Targetable within WorldRadius drawn as a blip. No terrain - just
// positions. Disc textures are generated once since IMGUI has no circles.
public static class Minimap
{
    public const float WorldRadius = 50f;
    public const float ScreenRadius = 70f;
    public const float Margin = 12f;

    private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color RimColor = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color SelfColor = Color.white;
    private static readonly Color PlayerColor = new Color(0.3f, 1f, 0.3f);
    private static readonly Color MobColor = new Color(1f, 0.35f, 0.3f);
    private static readonly Color TargetColor = Color.yellow;

    private static Texture2D discTexture;
    private static Texture2D blipTexture;

    public static void Draw(Transform self, Targetable currentTarget, IReadOnlyList<Targetable> blips)
    {
        EnsureTextures();

        float diameter = ScreenRadius * 2f;
        Rect rect = new Rect(UIScale.Width - diameter - Margin, UIScale.Height - diameter - Margin, diameter, diameter);
        Vector2 centre = rect.center;

        GUI.DrawTexture(rect, discTexture);

        foreach (Targetable blip in blips)
        {
            if (blip == null || blip.transform == self) continue;

            Vector3 offset = blip.transform.position - self.position;
            Vector2 flat = new Vector2(offset.x, offset.z);
            if (flat.magnitude > WorldRadius) continue;

            Color color = blip == currentTarget ? TargetColor : (blip.Stats != null && blip.Stats.GetComponent<PlayerMovement>() != null ? PlayerColor : MobColor);
            DrawBlip(centre + WorldToMap(flat), 6f, color);
        }

        // Heading: a short tick from the centre toward where the player faces.
        Vector2 forward = new Vector2(self.forward.x, self.forward.z).normalized;
        for (int i = 1; i <= 3; i++) DrawBlip(centre + WorldToMap(forward * (WorldRadius * 0.06f * i)), 3f, SelfColor);
        DrawBlip(centre, 7f, SelfColor);
    }

    private static Vector2 WorldToMap(Vector2 flat)
    {
        // World +z is "up" on the map; GUI y grows downward.
        return new Vector2(flat.x, -flat.y) * (ScreenRadius / WorldRadius);
    }

    private static void DrawBlip(Vector2 at, float size, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), blipTexture);
        GUI.color = previous;
    }

    private static void EnsureTextures()
    {
        if (discTexture != null) return;
        discTexture = MakeDisc(128, BackgroundColor, RimColor, rim: 2);
        blipTexture = MakeDisc(16, Color.white, Color.white, rim: 0);
    }

    private static Texture2D MakeDisc(int size, Color fill, Color rimColor, int rim)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        float radius = size * 0.5f - 0.5f;
        Vector2 centre = new Vector2(radius, radius);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), centre);
                Color color;
                if (distance > radius) color = Color.clear;
                else if (distance > radius - rim) color = rimColor;
                else color = fill;

                // Soften the outer edge by a pixel so the circle isn't jagged.
                if (distance > radius - 1f && distance <= radius) color.a *= radius - distance + 0.5f;
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
