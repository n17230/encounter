using UnityEngine;

// A melee attack profile - covers actual held weapons (a club, a sword)
// as well as natural weapons (claws, fists). Mobs and players both swing
// with these: EnemyAI reads whichever hand is attacking, PlayerAutoAttack
// reads the equipped main-hand item's weapon (or the unarmed fallback).
[CreateAssetMenu(menuName = "Encounter/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    public string WeaponName = "New Weapon";
    public float Damage = 10f;

    // Reach, horizontal centre-to-centre distance to the target.
    public float Range = 2f;

    // Seconds between swings.
    public float SwingInterval = 1.5f;

    // Status effect applied on hit. Null = none.
    public StatusEffectData Effect;
}
