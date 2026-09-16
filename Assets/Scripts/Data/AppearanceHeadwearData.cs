using UnityEngine;

// One selectable headwear piece - a standalone prefab (not baked into the
// body rig) parented onto the head bone at runtime by CharacterAppearance.
[CreateAssetMenu(fileName = "NewAppearanceHeadwear", menuName = "Encounter/Appearance Headwear")]
public class AppearanceHeadwearData : ScriptableObject
{
    // Stable identity used in saved profiles.
    public string Id;
    public string DisplayName;
    public AppearanceGender Gender = AppearanceGender.Unisex;
    public GameObject Prefab;
}
