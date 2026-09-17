using UnityEngine;

// Which idle/combat stance a weapon calls for - drives the player's
// weaponPose Animator parameter (see PlayerMovement.ResolveWeaponPoseParameter).
// OneHandShield is deliberately not a value here - shield is a property of
// the off-hand, not the weapon, so it's resolved by combining OneHand with
// CharacterEquipment/ProfileStore's own off-hand shield check. This enum's
// own declared ints (0-5, stored as-is on WeaponData assets) are NOT the
// same as the Animator parameter's ints (0-6) - ResolveWeaponPoseParameter
// remaps TwoHand/Staff/Bow/DualWield up by one to make room for the
// inserted OneHandShield=2, so e.g. a "PoseType: 2" on an asset means
// TwoHand, not the OneHandShield pose.
public enum WeaponPoseType { Unarmed, OneHand, TwoHand, Staff, Bow, DualWield }

// A melee attack profile - covers actual held weapons (a club, a sword)
// as well as natural weapons (claws, fists). Mobs and players both swing
// with these: EnemyAI reads whichever hand is attacking, PlayerAutoAttack
// reads the equipped main-hand item's weapon (or the unarmed fallback).
[CreateAssetMenu(menuName = "Encounter/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    // Fallback used only when there's no WeaponData to read a Range from
    // at all (shouldn't normally happen - every weapon asset, including
    // Fists, sets its own Range explicitly).
    public const float BasicAttackRange = 2f;

    public string WeaponName = "New Weapon";
    public float Damage = 10f;

    // Horizontal centre-to-centre reach for THIS weapon's basic (auto)
    // attack - melee weapons use BasicAttackRange (2), a ranged weapon
    // (e.g. Hunter's Bow) sets its own further reach. Basic-attack
    // *abilities* are not bound by this - they carry their own
    // AbilityData.Range.
    public float Range = BasicAttackRange;

    // Seconds between swings.
    public float SwingInterval = 1.5f;

    // Status effect applied on hit. Null = none.
    public StatusEffectData Effect;

    // Defaults to OneHand since most current weapons are 1H swords.
    public WeaponPoseType PoseType = WeaponPoseType.OneHand;
}
