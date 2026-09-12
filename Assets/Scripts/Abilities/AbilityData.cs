using UnityEngine;

[CreateAssetMenu(menuName = "Encounter/Ability", fileName = "NewAbility")]
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
    // Instant heal on resolve. Goes through the same HitInfo as Damage, so
    // a single ability could carry both (not currently used that way).
    public float HealAmount = 0f;
    // Absorb shield granted on resolve - see CharacterStats.ShieldAmount.
    public float ShieldAmount = 0f;
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

    // Status effect applied on hit (and refreshed by this ability's ground
    // patches). Null = none.
    public StatusEffectData Effect;

    // Duration used only for the effect applied directly to the primary
    // target on impact - ground patches always use Effect.Duration.
    // 0 means "no override, use Effect.Duration for the primary target too".
    public float DirectHitEffectDuration = 0f;

    // Ground-targeted (WoW "Blizzard"-style) casting: instead of selecting a
    // unit, the player aims a reticle and clicks to confirm a point on the
    // ground within Range of the caster; RequiresTarget is ignored. On
    // resolve, every player/mob within GroundEffectRadius of that point is
    // affected - currently only a forced move (ForceSpeed, 0 = none):
    // pulled toward the center, or blasted radially outward from it if
    // PushAway is set.
    public bool IsGroundTargeted = false;
    public float GroundEffectRadius = 0f;
    public float ForceSpeed = 0f;
    public bool PushAway = false;

    // Unit-targeted (RequiresTarget) instant teleport of the target to the
    // caster's position on resolve, instead of dealing damage - takes
    // priority over ProjectilePrefab if both are somehow set.
    public bool RecallTarget = false;

    // At most one currently-affected target across all of THIS caster's
    // casts of this ability (not a global limit - two different casters
    // can each have their own target). Casting it on someone new strips
    // Effect from whoever had it (see PlayerAbilities.ResolveAbility).
    // For abilities whose point is a single ongoing bond, e.g. One For All.
    public bool ExclusiveSingleTarget = false;

    // No unit/ground targeting at all - resolves centered on the caster's
    // own position, affecting every player (caster included) within
    // GroundEffectRadius. RequiresTarget should be false for these.
    public bool AreaAroundCaster = false;

    // Like AreaAroundCaster, but the opposite audience: every enemy (a
    // Targetable that is NOT a player, i.e. has no PlayerMovement) within
    // GroundEffectRadius of the caster's own position, in a full circle.
    // RequiresTarget should be false for these (e.g. Reaper's Wheel,
    // Seismic Slam).
    public bool EnemiesAroundCaster = false;

    // Like EnemiesAroundCaster, but only enemies within ConeAngle degrees
    // of the caster's facing (see FacingCone), not a full circle. Also
    // uses GroundEffectRadius for its reach. RequiresTarget should be
    // false for these (e.g. Cleave).
    public bool ConeAroundCaster = false;
    public float ConeAngle = 90f;

    // Casting requires an equipped MainHand weapon (CharacterEquipment.
    // MainHandWeapon != null) - fists don't count. Checked both client-side
    // (precheck) and server-side (authoritative).
    public bool RequiresMeleeWeapon = false;

    // Casting requires an equipped OffHand item with ItemData.IsShield set
    // (CharacterEquipment.HasShieldEquipped). Checked both client-side
    // (precheck) and server-side (authoritative). E.g. Aegis of the Ancient.
    public bool RequiresShield = false;

    // Total damage on resolve is Damage (above) PLUS this fraction of the
    // caster's currently equipped weapon's Damage (or the unarmed
    // fallback) - e.g. 1 = adds a full weapon hit (Reaper's Wheel,
    // Cleave, both with Damage 0, so it's the whole hit), 0.25 = adds a
    // quarter of a weapon hit on top of Damage (Crippling Blow). 0 (the
    // default) means no weapon scaling at all - see
    // PlayerAbilities.ResolveTotalDamage.
    public float WeaponDamagePercent = 0f;

    // Self-movement, no targeting: on resolve the caster charges straight
    // forward (their own current facing) for this many units, hitting
    // (Damage + Effect) every enemy near the path along the way. 0 = not
    // a charge ability. RequiresTarget should be false (e.g. Trample).
    public float ChargeForwardDistance = 0f;

    // Self-movement toward a unit target: on resolve the caster charges to
    // just short of the target's position (a gap-closer), then Effect is
    // applied to the TARGET (not the caster) - for support "peel" style
    // abilities (e.g. Team Up). RequiresTarget should be true.
    public bool ChargeToTarget = false;

    // Shared speed (units/sec) for ChargeForwardDistance and ChargeToTarget.
    public float ChargeSpeed = 20f;

    // Cast once, no target: grants a permanent, party-wide aura centered on
    // the caster - Effect (above) is pulsed to everyone within AuraRange
    // (self included), and/or AuraReveals is granted on the caster's own
    // minimap, exactly like the old gear-based auras did, except there's no
    // gear to wear and no duration - it simply never expires on its own.
    // At most one aura spell is active per caster at a time: casting a
    // DIFFERENT one replaces whichever was active (see
    // CharacterEquipment.SetActiveAura). RequiresTarget should be false.
    public bool IsAuraSpell = false;
    public float AuraRange = 40f;
    public MinimapReveal AuraReveals = MinimapReveal.None;

    // Unit-targeted (RequiresTarget), no damage/heal: removes one currently
    // active negative effect (StatusEffectData.IsNegative) from the target,
    // chosen arbitrarily if more than one is active - e.g. Cleanse.
    public bool RemovesNegativeEffect = false;

    // Ground-targeted (IsGroundTargeted should also be set), no damage: on
    // resolve, spawns StructurePrefab (a PlacedStructure - a real,
    // non-trigger obstacle, not a GroundPatch hazard trigger) at the aimed
    // point, oriented so its width axis is perpendicular to the caster's
    // facing at that moment (i.e. "across" whatever's directly ahead of
    // them). It has no lifetime of its own - it stays until THIS caster
    // casts this same ability again, which despawns the old one first (at
    // most one such structure per caster per ability - see
    // PlayerAbilities.activeStructures). E.g. Earthen Bastion.
    public bool IsPersistentStructure = false;
    public GameObject StructurePrefab;
    public float StructureWidth = 0f;
    public float StructureHeight = 5f;
    public float StructureThickness = 2f;

    // Unit-targeted (RequiresTarget), no damage: spawns FollowingZonePrefab
    // centered on the target, which then tracks the target's position for
    // PatchDuration seconds, reapplying Effect to everyone caught within
    // GroundEffectRadius (both fields reused from the ground-patch system -
    // same meaning, just driving a moving zone instead of a fixed one).
    // E.g. Arctic Winds.
    public bool IsFollowingZone = false;
    public GameObject FollowingZonePrefab;

    // No targeting at all: on resolve, Effect is applied directly to the
    // CASTER's own CharacterStats - not an ally/enemy AoE, not a
    // permanent aura, just a plain timed self-buff. RequiresTarget
    // should be false. E.g. Aegis of the Ancient.
    public bool SelfBuff = false;
}
