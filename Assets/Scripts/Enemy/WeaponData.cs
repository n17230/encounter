using UnityEngine;

// A melee attack profile - covers actual held weapons (a club) as well as
// natural weapons (claws). EnemyAI reads Damage/Effect off whichever hand's
// weapon is swinging; a null OffHandWeapon means the mob only attacks with
// MainHandWeapon.
[CreateAssetMenu(menuName = "reallyfungame/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    public string WeaponName = "New Weapon";
    public float Damage = 10f;

    // Status effect applied on hit. Null = none.
    public StatusEffectData Effect;
}
