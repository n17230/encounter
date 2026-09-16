using UnityEngine;

// One freely-toggleable cosmetic accessory (a cape, pauldrons, a neck scarf,
// etc.) - a specific named child GameObject inside the rig, same mechanism
// as AppearancePieceData, but not a single-choice slot: any number of
// accessories can be active at once, independent of each other and of which
// class each originally shipped with.
[CreateAssetMenu(fileName = "NewAppearanceAccessory", menuName = "Encounter/Appearance Accessory")]
public class AppearanceAccessoryData : ScriptableObject
{
    // Stable identity used in saved profiles.
    public string Id;
    public string DisplayName;
    public AppearanceGender Gender = AppearanceGender.Male;
    // Name of the child GameObject to enable inside the matching gender's
    // rig root, e.g. "M_Knight_Pauldrons" - see CharacterAppearanceApplier.
    public string NodeName;
}
