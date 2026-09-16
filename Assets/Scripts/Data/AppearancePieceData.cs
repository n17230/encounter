using UnityEngine;

// One selectable Top or Bottom cosmetic piece - a specific named child
// GameObject inside the BasicHero_M/BasicHero_F rig (see
// CharacterAppearance), not a prefab of its own. Gender-specific because
// the pack's Top/Bottom meshes are separate per gender rig.
[CreateAssetMenu(fileName = "NewAppearancePiece", menuName = "Encounter/Appearance Piece")]
public class AppearancePieceData : ScriptableObject
{
    // Stable identity used in saved profiles.
    public string Id;
    public string DisplayName;
    public AppearanceGender Gender = AppearanceGender.Male;
    public AppearanceSlot Slot = AppearanceSlot.Top;
    // Name of the child GameObject to enable inside the matching gender's
    // rig root, e.g. "M_Knight_Top" - see CharacterAppearance.
    public string NodeName;
    // Hair slot only, otherwise empty: the "clip-safe" node to show instead
    // of NodeName when headwear is currently selected (e.g. "M_hair_2b" for
    // NodeName "M_hair_2"), so the hairstyle doesn't poke through the hat.
    // Not every hairstyle has one - see CharacterAppearanceApplier.ApplyHair.
    public string HeadwearVariantNodeName;
    // Bottom slot only, otherwise empty: an AppearanceAccessoryData Id this
    // piece was designed to be worn with (e.g. Sorcerer's Bottom leaves the
    // shins bare without its matching Shoes) - auto-enabled the moment this
    // piece is picked (see MainMenu's Bottom tab), not force-kept-on
    // afterward, so the player can still turn it back off if they want.
    public string RequiredAccessoryId;
    // Bottom slot only, otherwise unused: whether this piece fully encloses
    // the leg. True (the common case, e.g. pants/armor) hides the base-skin
    // legs underneath so bare skin never pokes through the clothing. False
    // (skirts, e.g. Female Archer's) leaves the base-skin legs visible,
    // since the piece is deliberately short and the character's own legs
    // are meant to show below the hem.
    public bool CoversLegs = true;
}
