using UnityEngine;

[CreateAssetMenu(menuName = "reallyfungame/Ability", fileName = "NewAbility")]
public class AbilityData : ScriptableObject
{
    // Stable identity used over the network and in saved profiles. Never
    // change once content ships; the asset name/order can change freely.
    public string Id;
    public string AbilityName = "New Ability";
    public bool RequiresTarget = true;
    public float Range = 30f;
    public float Cooldown = 1.5f;
    public float CastTime = 0f;
    public float ManaCost = 10f;
    public float ThreatValue = 0f;
    public float Damage = 10f;
    public float MissileSpeed = 15f;
    public GameObject ProjectilePrefab;

    // Purely cosmetic - played at the caster's hands for the duration of
    // the cast (CastTime), separate from ProjectilePrefab which only fires
    // once the cast resolves. Not a NetworkObject - broadcast via ClientRpc
    // and instantiated locally on every client instead, since it carries no
    // gameplay state of its own.
    public GameObject CastVfxPrefab;

    public GameObject GroundPatchPrefab;
    public int MinPatchCount = 0;
    public int MaxPatchCount = 0;
    public float PatchScatterRadius = 3f;
    public float PatchRadius = 1.5f;
    public float PatchDuration = 6f;

    public DebuffType Debuff = DebuffType.None;
    public float DebuffMagnitude = 0f;
    public float DebuffTickInterval = 1f;
    public float DebuffDuration = 3f;

    // Duration used only for the debuff applied directly to the projectile's
    // primary target on impact - ground patches always use DebuffDuration.
    // 0 means "no override, use DebuffDuration for the primary target too".
    public float DirectHitDebuffDuration = 0f;
}
