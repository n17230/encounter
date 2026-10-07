Claude, this is a list of things you need to review. Please follow these instruction.
no
 - Batch reads. Before reading any code, list every file the punch-list items reference and read each one exactly once, even if multiple items touch it. Don't re-open a file already read.
 - Group by subsystem, not by list order. Cluster items that hit the same script/asset (e.g. all ground-targeted-casting items, all status-effect items) and diagnose each cluster in one pass instead of round-tripping per item.
 - No narration. Don't describe what you're about to do or summarize your process. Go straight to reading, then straight to output.
 - Do not report on things that are okay as is. Only point out issues or non-ideal things you find.
 - Terse output only. For each open item, one line: item name, verdict (likely bug / needs live test / blocked on Editor step), and a single sentence or 2 describing the problem. and then 1 sentence or 2 describing the fix.
 - If something requires live/server testing to resolve either way, say that in three words or less ("needs live test") rather than speculating at length.
 - you may speak between caveman and normal to help reduce token usage. Do not go 100% caveman.
 - Each of the items below are new mechanics or things I need you to review (Note: the enumeration has gaps due to deleting items). If you think a refactor is in place, feel free to move things around. Note - at the time you are reading this, everything works correctly.

## 1. Mana orb mob drops

- **Every mob now has a flat 5% chance to drop a mana orb on death**
  (`EnemyAI.HandleDeath` → `ManaOrb.TrySpawn`), which restores 250 mana
  to whichever player touches it, then disappears. **This one needs an
  Editor step before it does anything at all**: the pickup prefab has
  to live at exactly `Assets/Resources/Prefabs/ManaOrb.prefab` (loaded
  by path, not wired to a field, so every mob picks it up automatically
  with no per-prefab setup) — see "Not yet done" #8 in `CLAUDE.md` for
  the exact steps. Until that prefab exists, the drop roll still
  happens but silently no-ops (a console warning, no orb).

## 2. Arctic Winds (NEEDS AN EDITOR STEP BEFORE IT WORKS AT ALL)

- **Arctic Winds cannot be tested yet** - same situation as Earthen
  Bastion below: the gameplay code is written and compiles, but there's
  no zone prefab assigned, so casting it will currently just fizzle
  with "Zone not configured yet". **Before testing, do this in the
  Editor**: see "Not yet done" #7 in `CLAUDE.md` for the exact steps.
  - **Once wired up, please test**: target an ally (or a mob) and cast
    it - confirm a dome appears centered on them and actually **follows
    them** as they move for the full 8 seconds, slowing anyone standing
    inside by 10% (repeatedly, so someone who stays in it the whole
    time should show the slow the whole time, not just once). **Confirm
    it does NOT slow you, the caster** - even if you're standing inside
    your own dome, per your explicit follow-up. Confirm it despawns on
    its own after 8s. 300 mana / 30s cooldown / 8s duration / 25-unit
    diameter all came from you; range 30 and instant cast are
    unconfirmed placeholders.

## 3. Earthen Bastion (NEEDS AN EDITOR STEP BEFORE IT WORKS AT ALL)

- **Earthen Bastion cannot be tested yet** - all the gameplay code is
  written and compiles, but there's no wall prefab assigned, so casting
  it will currently just fizzle with "Structure not configured yet".
  **Before testing, do this in the Editor**: see "Not yet done" #6 in
  `CLAUDE.md` for the exact steps.
  - **Once the prefab is wired up, please test**: cast it facing a
    direction, confirm a 25-unit-wide wall appears roughly where your
    reticle was, oriented across (perpendicular to) the direction you
    were facing - and that you and mobs genuinely can't walk through it
    (a real physical block, not a visual-only prop). Cast it again
    (anywhere) and confirm the OLD wall disappears the instant the new
    one appears - never two walls from the same caster at once. Confirm
    a DIFFERENT player casting their own Earthen Bastion doesn't remove
    YOUR wall (per-caster, like One For All's bond). 350 mana / 10s
    cooldown came from you; height (5) and thickness (2) were never
    specified, pure placeholders - easy to retune once you can see it
    in-game.

## 4. One-shot attack animations got cut short by idle logic while standing still (fixed)

- **Bug**: `Attack_TwoHand`/`Attack_Bow` (and the new `Attack_Magic`,
  added for Firebolt/Icebolt/Soul Siphon's cast swing) could get
  visually cut off almost immediately if you were stationary when the
  animation started - it only played out fully while moving. Root
  cause: `CharacterIdle_M/F.controller`'s 7 weaponPose-based idle-swap
  Any State transitions had no exit-time gate, only `weaponPose == N`
  + `speed < 0.1`, so the instant you stood still one of them was
  continuously eligible and snapped straight back to the matching idle
  before the one-shot attack clip had played.
- **Fix**: added `AttackAnimationGuard`
  (`Scripts/Player/AttackAnimationGuard.cs`, a `StateMachineBehaviour`)
  to all three attack states in both controllers - sets a new
  `isAttacking` bool true on enter, false on exit. All 7 idle-swap
  transitions now also require `isAttacking == false`, so they can no
  longer preempt an attack in progress, standing still or not.
  **Please test**: melee auto-attack (two-handed weapon and a bow) and
  cast Firebolt/Icebolt/Soul Siphon while standing completely still -
  confirm the swing/cast animation now plays out fully before
  returning to idle, matching what already worked while moving.
  - **Second bug found while testing this**: casting a spell while
    stationary dealt damage/spent mana immediately but the
    `Attack_Magic` animation didn't visibly play until you moved
    (sometimes several seconds later). Cause: Unity's Any State
    transitions are evaluated in list order, first-satisfied-wins per
    frame - `castAttack → Attack_Magic` had been appended to the END
    of `m_AnyStateTransitions`, after all 7 idle-swap transitions, so
    while stationary an idle-swap transition (earlier in the list, also
    satisfied) won every frame and the trigger just sat queued;
    melee's `attack` transitions were already positioned before the
    idle-swap entries, which is why melee never showed this. Fixed by
    moving `castAttack → Attack_Magic` to the same priority tier as the
    melee attack transitions (right after them, before the idle-swap
    entries) in both controllers.
  - **Third adjustment**: the cast-attack animation's lead time (how long
    before the cast actually resolves it starts playing) was bumped from
    0.05s to 0.15s per your request
    (`PlayerAbilities.CastAnimationLeadTime`). Instant-cast abilities
    (currently only Soul Siphon) previously fired the animation and
    resolved in the same instant - now they also wait the full 0.15s
    lead time before resolving (`StartServerCast`'s `totalWait =
    Mathf.Max(ability.CastTime, CastAnimationLeadTime)` when
    `PlaysCastAttackAnimation` is set), so the animation always has time
    to play before the effect lands, matching cast-time abilities. This
    is a genuine (if tiny) gameplay timing change for Soul Siphon, not
    purely cosmetic - it now spends mana/deals its effect 0.15s later
    than before. GCD (1.5s) already dwarfs this, so no other system is
    affected. **Please test**: Soul Siphon still feels instant/responsive
    at 0.15s delay; if not, this constant is trivial to retune.

## 5. Projectiles now spawn from the caster's right hand (Firebolt/Icebolt)

- Previously spawned at a fixed offset from the root
  (`transform.position + up*1.5 + forward*0.5`), not from any bone.
  `PlayerAbilities.ResolveAbility`'s projectile branch now uses
  `CharacterAppearance.GetRightHandBone()` first, falling back to the old
  offset if unresolvable.
- **This needed a real fix, not a one-off hack**: `ResolveAbility` runs
  server-side only, but `CharacterAppearance.Apply()` previously resolved
  `ActiveAnimator`/rig activation only behind `if (!IsClient) return` -
  meaning on the actual dedicated server (not Host), the correct
  gender's rig GameObject was never `SetActive`'d at all, so the hand
  bone would've always resolved off whichever rig the prefab defaults to,
  frozen at rest pose. Fixed by hoisting the gender-rig `SetActive`
  toggle and `ActiveRigRoot`/`ActiveAnimator` resolution out from behind
  that gate so they run on server and client alike; only the actual
  clothing mesh/material/recolor swap stays client-only (still invisible
  and pointless on a headless server). Verified via
  `NetworkAnimator`'s own source
  (`Library/PackageCache/com.unity.netcode.gameobjects@.../Runtime/
  Components/NetworkAnimator.cs`) that its parameter/crossfade sync RPCs
  target `SendTo.NotAuthority`, which includes the server for this
  project's owner-authoritative setup - so the server's now-active
  Animator should already be getting driven to match the owner's real
  pose, not sitting inert.


## 6. Hunter's Bow now fires a visible arrow at the target

- Basic ranged auto-attack was previously a pure hitscan with no visual
  projectile at all. `WeaponData` gained `ArrowPrefab`/`ArrowMissileSpeed`
  (opt-in, every other weapon leaves `ArrowPrefab` null);
  `WeaponHuntersBow.asset` points it at the `ArrowBow2_1` model (Blink
  pack, copied into `Assets/External/Blink/...` along with its
  mesh/material/textures).
- **No nocked-arrow-during-the-draw visual** - tried that first, you asked
  to drop it. The arrow only appears at the moment the shot actually
  resolves (spawned fresh at the bow's current position via
  `CharacterWeaponVisual`'s new `MainHandModelTransform`), then flies to
  the target over `distance / ArrowMissileSpeed` seconds and is destroyed.
- **Deliberately kept purely cosmetic** - no new `NetworkObject`, no
  change to auto-attack's existing fixed-schedule damage timing (unlike
  `Projectile.cs`'s spell projectiles, whose own travel time decides when
  damage lands). Every client independently instantiates/destroys the
  arrow via `[ClientRpc]` (`PlayerAutoAttack.PlayArrowShotClientRpc`),
  same pattern as `PlayCastVfxClientRpc`/`CharacterWeaponVisual`.
- **One real bug found and fixed during review**: if the shooter
  disconnects/despawns mid-flight, the flight coroutine (which lives on
  the shooter's own component) gets killed without reaching its final
  `Destroy(arrow)`, leaking the now-unparented arrow in the scene
  permanently; fixed with a redundant scheduled `Destroy(arrow, duration)`
  set independently of the coroutine, right when the flight starts. Also
  guarded a zero-distance `LookRotation` (point-blank shot) that would've
  logged a harmless but noisy Unity console error.
- **Asset-porting gotcha worth knowing about for future pack imports**:
  copying `ArrowBow2_1` (+ its mesh/material/textures) into
  `Assets/External` while the original still exists locally at
  `Assets/Blink/...` caused Unity to detect a GUID collision and silently
  reassign new GUIDs to all 7 of the newly-discovered tracked copies -
  but it does NOT rewrite the internal cross-references inside those same
  files (the prefab's own mesh/material references, the material's own
  texture references), leaving them pointing at the old GUIDs now owned
  by the untracked originals. Had to hand-fix every internal reference to
  match the reassigned GUIDs so the tracked subset is actually
  self-contained (would otherwise show missing mesh/textures on a fresh
  clone without the local pack folder). Worth double-checking this same
  way any time a multi-file asset gets copied into `Assets/External`
  while its source pack is still present locally.

## 7. Mob pathfinding via NavMesh - PathRecalcInterval is a flagged default

- `EnemyAI.ComputeChaseDirection` recalculates each mob's NavMesh path at
  most every `PathRecalcInterval` (0.25s), not every physics tick -
  **not a value the user specified**, picked as a reasonable default the
  same way `globalCooldownDuration` (1.5s GCD) was. Worth tuning after
  playtesting with real terrain geometry: too long and chasing looks
  laggy/wobbly at corners; too short and it burns CPU recalculating paths
  for no visible benefit.
- No NavMesh exists in the project yet - this only takes effect once the
  user installs `com.unity.ai.navigation` via Package Manager and bakes a
  `NavMeshSurface`. Until then, every mob keeps chasing in the old
  straight-line way (the fallback path in `MobPathing.DirectionTowardPath`
  is always taken), so this is a non-breaking rollout either way.
- **One real bug found and fixed during review**: the first version read
  `NavMeshPath.corners` every `FixedUpdate` tick, not just on recalc -
  that property allocates a brand new array on *every single access*
  (confirmed via Unity's own documented rationale for `GetCornersNonAlloc`
  existing at all), so it was allocating 50x/sec per chasing mob instead
  of the intended ~4x/sec. Fixed by reading corners via
  `GetCornersNonAlloc` into a reused buffer array only inside the
  recalc-gated branch, with a separate count field (not the buffer's own
  `Length`) tracking how many of its slots are this path's real corners
  vs. stale leftovers from a previous, longer path.

