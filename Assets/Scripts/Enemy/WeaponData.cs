using UnityEngine;

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
}
