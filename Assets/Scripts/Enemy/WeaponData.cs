using UnityEngine;

// A melee attack profile - covers actual held weapons (a club) as well as
// natural weapons (claws). EnemyAI reads Damage/Debuff off whichever hand's
// weapon is swinging; a null OffHandWeapon means the mob only attacks with
// MainHandWeapon (see EnemyAI.isDualWielding-free design).
[CreateAssetMenu(menuName = "reallyfungame/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    public string WeaponName = "New Weapon";
    public float Damage = 10f;

    public DebuffType Debuff = DebuffType.None;
    public float DebuffMagnitude = 0f;
    public float DebuffTickInterval = 1f;
    public float DebuffDuration = 3f;
}
