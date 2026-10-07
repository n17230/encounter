using UnityEngine;

// Which equip socket a held model attaches to. SlotDefault = MainHand items
// go to the right-hand socket, OffHand items to the left; the explicit
// values exist for weapons whose animations hold them in the other hand
// (e.g. a bow: MainHand item, but held in the left hand while the right
// draws).
public enum AttachHand
{
    SlotDefault,
    Right,
    Left,
}

// How one *category* of held model (swords, staves, shields...) sits in an
// equip socket - shared by every item of that category rather than tuned
// per item, since models from the same pack/type share a pivot convention
// (same reasoning as CharacterAppearanceApplier's single shared headwear
// offset). An outlier that doesn't fit its category just gets its own
// profile asset. Referenced directly by ItemData.AttachProfile, like
// WeaponData - not Id-looked-up. Values are socket-local and are normally
// written by the Weapon Attach Tuner (Encounter menu), not typed by hand.
[CreateAssetMenu(fileName = "NewWeaponAttachProfile", menuName = "Encounter/Weapon Attach Profile")]
public class WeaponAttachProfile : ScriptableObject
{
    public Vector3 Position;
    public Vector3 Rotation;
    public float Scale = 1f;
    public AttachHand Hand = AttachHand.SlotDefault;
}
