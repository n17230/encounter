using UnityEngine;

// Singleton asset (one instance, loaded by fixed path - see GameDatabase)
// holding the pack's fixed body-skin and clothing/gear recolor textures.
// Referenced by plain array index rather than by Id like every other data
// asset here: this is a bounded, pack-defined palette that won't grow
// independently of the pack itself, so 22 near-empty single-texture Id
// assets would be disproportionate ceremony. Reordering these arrays would
// rebind saved profiles - acceptable for a fixed art-pack palette.
[CreateAssetMenu(fileName = "AppearanceColorPalette", menuName = "Encounter/Appearance Color Palette")]
public class AppearanceColorPalette : ScriptableObject
{
    // The pack ships two color systems on the same meshes: a runtime-
    // tintable shader-graph material (RGBRecolor_Body/Objects, using
    // exposed color properties over a single shared mask texture) and,
    // separately, these flat pre-baked swatch textures meant for a plain
    // URP/Lit material (PreColor_Body/Objects). CharacterAppearance reassigns
    // each renderer from the shader-graph material to these once, then
    // swaps which swatch texture is bound via MaterialPropertyBlock -
    // simpler than driving the shader graph's own per-region properties.
    public Material BodyMaterial;
    public Material ObjectMaterial;
    public Texture2D[] BodyColors;
    public Texture2D[] ObjectColors;
}
