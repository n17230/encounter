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

    // Anything that reaches past melee range is a ranged weapon - the one
    // definition shared by block (melee only), Arcane Shield (ranged only),
    // and auto-attack line of sight, rather than each re-deriving it.
    public bool IsRanged => Range > BasicAttackRange;

    // The HitSource this weapon's basic attack lands as.
    public HitSource BasicAttackSource => IsRanged ? HitSource.Ranged : HitSource.Melee;

    // Seconds between swings.
    public float SwingInterval = 1.5f;

    // If set, this weapon's basic attack heals the target for this amount
    // INSTEAD of dealing Damage (mutually exclusive - see
    // PlayerAutoAttack.FixedUpdate, which zeroes Damage for the swing
    // whenever this is > 0, so equipped WeaponDamageBonus gear can't leak
    // incidental damage through a healing weapon). 0 (every damage-dealing
    // weapon) = normal behavior, unchanged. E.g. Holy Scepter.
    public float HealAmount = 0f;

    // Status effect applied on hit. Null = none.
    public StatusEffectData Effect;

    // Defaults to OneHand since most current weapons are 1H swords.
    public WeaponPoseType PoseType = WeaponPoseType.OneHand;

    // How long before the swing/shot actually lands the attack-swing
    // animation should start, so a long wind-up (e.g. drawing a bow)
    // finishes exactly as the hit resolves instead of both firing in the
    // same instant. 0 (default, every existing weapon) = play the
    // animation at the same moment the hit lands, same as before this
    // field existed.
    public float AttackAnimationLeadTime = 0f;

    // Purely cosmetic - the physical arrow spawned at the bow's position
    // and fired toward the target when the shot resolves. Null (every
    // non-bow weapon) = no visual arrow at all. See
    // PlayerAutoAttack.PlayArrowShotClientRpc.
    public GameObject ArrowPrefab;

    // Units/sec for the fired arrow's cosmetic flight from bow to target -
    // purely visual, has no bearing on when the hit actually lands (that's
    // still the fixed SwingInterval cadence, unchanged).
    public float ArrowMissileSpeed = 40f;

    // The flight direction is computed via Quaternion.LookRotation (+Z
    // forward), but an imported model's own authored "forward" axis often
    // doesn't match that convention - this Euler offset is applied on top
    // to correct it, tuned by eye (every arrow is short-lived and freshly
    // spawned, so just re-nudge this between auto-attack swings and watch
    // the next arrow - no special live-tuning hook needed).
    public Vector3 ArrowModelRotationOffset = Vector3.zero;

    // Local-space offset (relative to the equipped weapon model's own
    // position/rotation, via Transform.TransformPoint) for where the fired
    // VFX actually originates - most weapon models (e.g. Holy Scepter's
    // Wand_Epic) have no dedicated "tip"/"head" child transform to anchor
    // to, so this has to be tuned by eye instead. Vector3.zero (default) =
    // the weapon model's own root position, unchanged from before this
    // field existed.
    public Vector3 ArrowSpawnOffset = Vector3.zero;

    // Purely cosmetic - whether this weapon's auto-attack windup plays the
    // Cast_Heal animation/"castHeal" trigger (see PlayerAutoAttack
    // .FixedUpdate and PlayerAbilities.PlayCastHealAnimationClientRpc)
    // instead of the melee "attack" trigger, independent of whether the
    // swing actually heals (HealAmount) - lets a damage weapon (e.g. Staff
    // of the Magi) borrow the same visual as a healing weapon (Holy
    // Scepter) without becoming one itself. false (default, every existing
    // weapon except Holy Scepter) = unchanged, melee/no windup trigger per
    // the usual PoseType rule.
    public bool PlaysHealSwingAnimation = false;

    // Fast-forwards the fired VFX's particle systems to this many seconds
    // into their own timeline the instant they're spawned, via
    // ParticleSystem.Simulate() - there's no Inspector-only way to do this
    // for a one-shot (non-looping) effect. 0 (default) = play from its own
    // start, unchanged from before this field existed. Useful when a
    // prefab's first fraction of a second looks empty/still "building up"
    // before it's actually visible.
    public float ArrowAnimationStartOffset = 0f;
}
