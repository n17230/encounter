using UnityEngine;

// A melee attack profile - covers actual held weapons (a club, a sword)
// as well as natural weapons (claws, fists). Mobs and players both swing
// with these: EnemyAI reads whichever hand is attacking, PlayerAutoAttack
// reads the equipped main-hand item's weapon (or the unarmed fallback).
[CreateAssetMenu(menuName = "Encounter/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    // Every basic (auto) attack reaches this far (horizontal centre-to-
    // centre), mob or player, armed or unarmed. Melee *abilities* are not
    // bound by this - they carry their own AbilityData.Range.
    public const float BasicAttackRange = 2f;

    public string WeaponName = "New Weapon";
    public float Damage = 10f;

    // Seconds between swings.
    public float SwingInterval = 1.5f;

    // Status effect applied on hit. Null = none.
    public StatusEffectData Effect;
}
