# Things to review in the Editor

Everything below was written and compile-checked outside the Editor (via
`Tools/CompileCheck.csproj`, since the Editor usually holds the project
lock) but never actually run — I have no way to launch Unity or play the
game from here. This is the punch list of what to playtest, roughly in
priority order. Delete items as they're confirmed working; delete the
whole file once it's empty.

## 0. Buff/aura/spell VFX indicators - two real bugs found from first playtest, fixed

- **First playtest found two real bugs, both now fixed**:
  1. Aura VFX (`AuraGroundVisual`) appeared at the player's center, not
     their feet - `transform.position` sits at the `CharacterController`
     's center, not the ground; fixed to compute actual feet Y the same
     way `PlayerMovement.ValidateReplicatedMovement` already does
     (`center.y - height/2` offset), instead of assuming
     `transform.position.y` was already ground-level.
  2. `EffectOverheadVisual` (Buff_Light on Vitality Ward) never showed
     at all - it subscribed to `NetworkList.OnListChanged`, a pattern
     not used/proven anywhere else in this codebase (the one existing
     reader of this same list, `PlayerHUD.DescribeEffects`, polls it
     fresh every frame instead). Switched to polling, matching that
     proven approach.
  - **`ShieldVisual` fixed proactively too, even though not reported
    broken yet** - it used the equivalent `NetworkVariable.OnValueChanged`
    subscription, the same untested-in-this-codebase pattern as bug 2
    above, just on a plain variable instead of a list. Switched to
    polling as well, before you hit the same bug a third time. Please
    specifically re-check Aegis of Arcane's shield VFX now, since it
    was never confirmed working in the first place.
  3. **Third bug, found from your follow-up report** ("has the glow but
     not the actual aura pattern / shield icon"): every particle layer
     in `Aura_Arcane`, `Aura_Light`, and `Buff_Light` was still set to
     World simulation space (same setting that caused the earlier
     Firebolt/Icebolt tracking bug). Scale-pulsing a World-space layer
     doesn't reliably work - a particle's size locks in at the moment
     it's emitted, so any layer whose particles happened to spawn while
     the pulse was near-zero-scale stayed invisible permanently, even
     once the scale ramped back up; only short-lived layers (Glow)
     re-emit often enough to mostly look right regardless. Flipped
     every layer in all three prefabs to Local space. **Not visually
     confirmed by me** (can't run the Editor) - please recheck all
     three (shield, buff, both auras) for the full effect now, not just
     the glow layer.
- **New: `ShieldVisual` component (`Player.prefab`)** shows the
  `Shield_Arcane` VFX (from the newly-externalized `Assets/External/
  Spells Pack/Particles/Prefabs/Shields/`) on whoever is currently
  shielded, for as long as `CharacterStats.ShieldAmount` stays above 0
  - disappears the instant the shield is fully absorbed or a fresh cast
  replaces it. Reacts directly to the `ShieldAmount` `NetworkVariable`
  (already synced to everyone), so every client sees it on the
  shielded player, not just the shielded player themselves.
- Since this keys off `ShieldAmount` generically rather than "Aegis of
  Arcane specifically," it'll also show on ANY future ability that
  grants a shield the same way (currently only Aegis of Arcane does) -
  worth knowing if a second shield spell is ever added with a different
  intended look.
- **Please confirm**: cast Aegis of Arcane on a target, confirm the
  shield VFX appears on them immediately and stays through however
  many hits it takes to burn through the 350 absorb - confirm it
  disappears the instant the shield hits 0, and that recasting it while
  the old shield is still up doesn't leave two VFX instances stacked
  (should just be one, since a shield replaces rather than stacks).
- **New: `EffectOverheadVisual` component (`Player.prefab`)** shows
  Buff_Light above a player's head for as long as they have Vitality
  Ward (Blessing of Vitality's +10 Armor buff) active - generalized off
  `CharacterStats.ActiveEffects` (any `StatusEffectData` could be wired
  the same way; only Vitality Ward is wired right now). Please confirm:
  cast Blessing of Vitality on a target, Buff_Light appears above their
  head immediately and disappears exactly when the 16s Vitality Ward
  buff expires.
- **New: `AuraGroundVisual` component (`Player.prefab`)** shows
  Aura_Arcane at a player's feet while they have Aura of Replenishment
  slotted, and Aura_Light at their feet while they have Aura of
  Regeneration slotted (both can show at once if both are slotted).
  Needed two new synced flags on `CharacterEquipment`
  (`HasReplenishmentAura`/`HasRegenerationAura`) since nothing
  previously told OTHER clients which specific aura a player has -
  only that player's own profile knew. Size is 1x the prefab's own
  natural size (`AuraGroundVisual.sizeMultiplier`), set once at spawn.
  - **Root cause of "only the glow shows, not the actual pattern"
    (three rounds of back-and-forth to track down)**: it was never the
    shader/material (though that conversion was real and needed - these
    materials were on a Built-in-only shader URP can't render at all),
    and never the World-vs-Local simulation space (also real, also
    needed for correct tracking). The actual remaining bug was in my
    own code - I was fading the aura in/out by animating its
    `transform.localScale` every frame, but a particle's size is baked
    in at the moment it's emitted, not continuously re-read from the
    parent's current scale. So any particle that happened to spawn
    while the pulse was near-zero-scale stayed invisible forever, even
    once the scale ramped back up - only very short-lived layers
    (re-emitting fast enough that few particles were ever "born small")
    happened to look mostly fine, which is exactly the "just the glow"
    symptom. Confirmed by you finding a plain, untouched instance
    renders completely fine directly in the Scene. Rewrote the fade to
    modulate each `ParticleSystemRenderer`'s material alpha instead
    (captured once per renderer from the original design alpha, so
    e.g. an intentionally-70%-transparent layer still ends up at 70%
    at the pulse's peak, not 100%) - this doesn't touch particle
    simulation at all, so it should be the real fix. **Not visually
    confirmed by me** - please recheck both auras for the first time
    since this rewrite.
  - **Pulse timing, worth watching closely since it was never run**: a
    repeating 6-second cycle, each player's own 3-second visible window
    (alpha fades 0 → full → 0, peaking exactly in the middle) starting
    on a different second based on their rank among connected players
    (1st to join starts at cycle-second 0, 2nd at 1, ... 6th at 5, then
    wraps). With 2+ players both running the same aura, confirm their
    pulses visibly stagger rather than flashing in unison - and that a
    player's own pulse timing doesn't jump/reset just because someone
    else joins or leaves (their rank could shift, which intentionally
    reseats their phase - not something to "fix", just something to
    notice if it looks jarring).
- **New: `AbilityData.TargetVfxPrefab`** - a target-side counterpart to
  the existing `CastVfxPrefab` (caster-side, during the cast). Blessing
  of Vitality now plays Casting_Light on the caster while casting and
  Spell_Light_6 on the target once the heal actually lands. Please
  confirm both show up and neither lingers oddly - the target VFX is
  destroyed after a flat 5s (same convention `Projectile
  .impactVfxPrefab` already uses), not tied to the actual particle
  system's own natural length.
- **Found and fixed in passing**: `Casting_Light.prefab` (used above)
  had a leftover enabled demo script (`CreateProjectile`, from the
  asset pack's own demo scene) that would have tried to spawn a whole
  separate "Projectile_Light" effect on a timer - the same class of
  leftover-demo-script issue the Firebolt/Icebolt VFX had earlier.
  Disabled it before wiring this in, so it shouldn't recur here.

## 1. Per-mob Animator Controllers, generic run clip - never run, touches every mob's animation

- **Every mob visual now has its own Animator Controller instead of
  sharing one** - Ogre and Ogre Brute used to point at the exact same
  `OgreHammerController`, and Skeleton Warrior was set to reuse it too
  (from an earlier fix). Split into six standalone duplicates so none
  of them can affect each other anymore: `OgreController` (Ogre),
  `OgreBruteController` (Ogre Brute), `SkeletonWarriorController`
  (Skeleton Warrior), `GoblinController` (Goblin), `GoblinBruteController`
  (Goblin Brute), `SkeletonArcherController` (Skeleton Archer) - all
  under `Assets/Animation/`. **Please confirm each of these six mobs
  still animates correctly** (idle/run/attack/death) - this touched
  every mob's Animator reference, so it's worth a look even for the
  ones whose actual clips didn't change (Ogre, Skeleton Warrior).
- **Goblin and Skeleton Archer previously had NO real animation at
  all** - both were pointed at the PolysplitGames pack's own demo
  controller (a single frozen "Pose" state, no parameters, no
  transitions), so before this they likely never actually
  idled/ran/attacked/died visually, regardless of what `EnemyAI` was
  telling them to do. They now have a real Idle/Run/Attack/Death state
  machine for the first time - **this is the riskiest part of this
  batch, worth the closest look**: summon a Goblin and a Skeleton
  Archer, confirm they idle when standing, transition to a run when
  chasing, play an attack animation on hit, and play a death animation
  on dying.
- **Run animation changed to `Stander@Jog`** (a generic humanoid clip
  from the new `Assets/Shinabro-combat` pack) for every mob EXCEPT Ogre
  and Skeleton Warrior, which keep their original run clip
  (`Stander@Sword&Shield_Run`) unchanged, per your explicit instruction.
  Confirm the new Jog cycle looks right at each mob's actual run speed
  (`EnemyAI.RunSpeed`) - a generic clip authored at one speed can look
  like sliding/moonwalking if the mob's actual movement speed doesn't
  match the clip's original pace; not something that can be checked
  without seeing it move.
- **Skeleton Archer got the pack's bow-specific Idle/Attack clips**
  (`Stander@Bow_Idle`/`Stander@Bow_Attack1`) instead of the generic
  sword&shield ones every other mob uses, since it's a ranged unit -
  Death still uses the generic `Stander@KnockDown_F_Light` (no
  bow-specific death clip exists in the pack). Confirm the bow idle/
  attack poses actually look right holding a bow, not just technically
  play.
- **Skeleton Mage/Healer/Tactician were explicitly left out of this
  batch** - they have no Animator component at all yet (not just an
  unwired controller, the component itself is missing), which is a
  bigger gap than what this batch fixed. Not addressed here.
- **New pack added**: `Assets/Shinabro-combat` (gitignored like the
  other four packs, added to `.gitignore`) - only the three clips
  actually used (`Stander@Jog`, `Stander@Bow_Idle`,
  `Stander@Bow_Attack1`) were moved into `Assets/External/Shinabro-combat/`
  following the existing pack convention.

## 2. UI Scale / Look Sensitivity are now sliders - never run

- **UI Scale** (Options page) changed from −/+ 25% buttons to a
  `GUILayout.HorizontalSlider`, same 75%–250% range and profile
  backing as before - just a control-style change, please confirm it
  drags smoothly and still clamps at both ends.
- **New: Look Sensitivity slider**, 0.25×–3× (placeholder range, not
  specified beyond "make a slider for look sensitivity") - a single
  multiplier (`PlayerProfile.LookSensitivity`, via the new
  `LookSensitivityScale` static, same pattern as `UIScale`) applied on
  top of `PlayerCamera`'s pitch/free-look-yaw sensitivity AND
  `PlayerMovement`'s right-drag turn sensitivity - one slider scales
  all mouse-look speed together, not three separate ones. 1× (the
  slider's default/center-ish) should feel identical to how the game
  already felt before this change. Please confirm: dragging it up
  makes both free-look (left-click-drag) and turning (right-click-drag)
  noticeably faster together, dragging it down makes both slower
  together, and it persists across a menu close/reopen and a
  restart (saved to the profile like UI Scale).

## 3. Camera zoom (Up/Down arrow) - never run

- **New control, not previously in the game**: holding Up Arrow zooms the
  camera in, Down Arrow zooms it out, along the camera's existing
  distance-behind-the-pivot offset - fixed keys, not rebindable, same
  category as Tab/backtick/F1-F5. Please confirm: hold Up, camera moves
  closer to you smoothly and stops at a minimum distance rather than
  clipping into your character; hold Down, camera moves away and stops
  at a maximum rather than drifting off indefinitely; release either key
  and it just holds wherever it was left (no snap-back).
- **Placeholder numbers, not specified beyond "up/down to zoom in/out"**:
  zoom speed (8 units/sec, `PlayerCamera.zoomSpeed`, not saved - a feel
  setting, not a preference), min/max distance (1.5/12,
  `CameraZoomScale.Min`/`Max`). The starting distance (4) is whatever
  the prefab already had, untouched.
- **Now saved to the profile** (`PlayerProfile.CameraZoomDistance`, via
  `CameraZoomScale` - same pattern as `UIScale`/`LookSensitivityScale`):
  written into the profile's in-memory copy on every zoom change, and
  persisted to disk by whichever existing Save() trigger fires next
  (closing the Escape menu, entering the testing area, etc.), same as
  UI Scale/Look Sensitivity - no new save trigger was added. Please
  confirm: zoom in/out, close and reopen the Escape menu (or
  disconnect/rejoin), and check the camera comes back at the distance
  you left it rather than resetting to the prefab's default (4).

## 4. AoE now hits anyone, players and mobs alike (behavior change)

- **Melee AoE (Reaper's Wheel, Cleave, Seismic Slam, Trample) can now hit
  other players, not just mobs** — and heal/shield AoE (Seraph's Grace)
  can now also affect nearby mobs, not just players. Previously these
  four were hard-filtered to one audience or the other; that filter is
  gone. The one thing that's still always excluded is the caster
  themselves for the three damage-dealing ones (you can't hit yourself
  with your own Cleave).
- **Please confirm**: cast Reaper's Wheel/Cleave/Seismic Slam near
  another player and a mob standing together — both should take damage,
  you shouldn't. Cast Trample charging past another player and a mob —
  same. Cast Seraph's Grace near a mob — it should now get healed too
  (worth flagging if that's surprising in practice, since it's a
  literal reading of "AoE hits anyone" applied to a heal, not something
  separately requested).
- **Fire/ice ground patches are a separate mechanic and unaffected by
  this** — `GroundPatch.cs`'s trigger already hits anyone who steps in
  it, caster included, and always has. Nothing changed there; just
  confirming it still hits you if you stand in your own Firebolt/Icebolt
  patch.

## 5. Slows no longer stack across different effects (new global rule)

- **Only the single strongest slow can ever be active on a character at
  once, permanently, for every slow in the game** — `StatusEffectData
  .IsSlow` now tags Icebolt's Slowed, Arctic Winds, Crippling Blow, and
  Tendon Shot. Landing a weaker slow while a stronger one is already up
  is a complete no-op (doesn't refresh it, doesn't do anything); landing
  a strictly stronger one immediately clears every other active slow.
  This is a genuine exception to how every other pair of different
  effects in the game works (they stack independently even touching the
  same stat) — scoped specifically to slows, forever, not just for this
  session's new spells.
- **Please confirm**: get slowed by Icebolt (−66%), then get hit by
  Tendon Shot (−50%) while it's still active — you should stay at −66%,
  not somehow end up slower than that. Then let Icebolt's Slowed expire
  naturally (or get Cleansed) while Tendon Shot's is still ticking —
  confirm you're now at the weaker −50% rather than back to full speed
  or stuck at 0. Also worth trying the reverse order (weaker first,
  then stronger) to confirm the stronger one properly evicts the weaker
  one rather than the two coexisting.
- **Why this exists**: before this, RunSpeed modifiers from different
  slow effects just summed like any other pair of stat modifiers -
  e.g. Icebolt (−66%) plus Tendon Shot (−50%) simultaneously would have
  driven RunSpeed to a −116% multiplier, which `Stat` doesn't clamp for
  RunSpeed (only Armor has a floor) - almost certainly negative/backward
  movement or a full stop well before this fix.

## 6. Skeleton Tactician escort encounter (biggest, newest, most untested batch yet)

- **A whole new boss encounter**, five mobs deep — see `BOSS_DESIGN.md`
  for the full design writeup. The Tactician is the boss; Warrior,
  Archer, Healer, and Mage are its escort. This needed a genuinely new
  mob-side ability-casting framework (`EnemyAI.MobRole` +
  role-specific trigger checks every `FixedUpdate`) that didn't exist
  before at all — mobs previously only ever did basic melee/ranged
  auto-attack, nothing conditional/triggered. **This is by far the
  riskiest, least-tested thing in this whole file** — it's never been
  run even once, touches the shared `EnemyAI`/`CharacterStats` code
  every other mob in the game also uses, and involves several pieces
  of genuinely new infrastructure (see below). Expect bugs.
- **Should actually be spawnable now**: all five prefabs are registered
  in `DefaultNetworkPrefabs.asset`, visuals are attached, and there's a
  one-button "Summon Skeletons" on the Summon Mobs panel that spawns
  the whole group (2 Warriors, Archer, Healer, Mage, Tactician) at once
  in a tight cluster, alongside the option to summon them individually
  like any other mob. **First real playtest already turned up a
  `NetworkAnimator.CheckParametersChanged` NullReferenceException**
  mid-fight (freeze + a graphics glitch) — still undiagnosed for
  certain, current best guess is one of the newly hand-attached
  skeleton visuals has an Animator setup issue that a mob death exposed
  mid-combat. Worth a fresh look before assuming any of the rest of
  this section's items are even reachable in a real fight yet.
- **New infrastructure, each worth confirming independently once the
  mobs can actually spawn**:
  - **Vision darkening** (Enshroud, Concussive Shot) — the affected
    player's own screen should fade to black past a short distance
    (`StatusEffectData.VisionRange`, via `RenderSettings.fog`). Confirm
    it only affects the debuffed player's screen, nobody else's, and
    that it correctly clears when the debuff expires (doesn't leave you
    permanently fogged).
  - **Arcane Shield** — a Mage-cast dome (no visible mesh, it's a pure
    server-side position+radius check, `ArcaneShieldZones`) that should
    block ranged weapons/spells from targeting whoever's standing
    inside it, melee unaffected. Confirm both a ranged basic attack
    (e.g. Hunter's Bow) and a ranged spell (e.g. Firebolt) get rejected
    against a shielded target, and that melee still lands normally.
  - **Tactical Instruction** — while the Tactician's alive, the escort
    should get a mana regen boost, immunity to Slow, and different
    targeting priorities (see `BOSS_DESIGN.md`); all of that should
    disappear the instant the Tactician dies. This is checked by
    distance each tick (`tacticalInstructionRange`, 120), not a real
    aura pulse — confirm it actually turns off if the escort wanders
    far enough from a dead-but-not-quite Tactician, or right at death.
  - **Tactician kiting/melee-switch** — opens at range, should
    permanently switch to melee once you get within 15 range of it or
    it's taken damage, or once the escort drops below 3 alive. This is
    the most speculative piece of AI in the whole batch (there's no
    precedent for "back away while attacking" anywhere else in the
    game) — watch closely for it getting stuck, oscillating in place,
    or never actually closing to melee.
- **Known, deliberate simplifications** (not bugs, but worth knowing
  before reporting them as bugs):
  - The Mage's "Icebolt" is a plain weapon (same damage + slow as the
    player spell) fired through the normal basic-attack path — it
    won't spawn ice ground patches the way the player's actual Icebolt
    cast does.
  - Archer repositioning (on the Mage's Enshroud) and the Tactician's
    kiting both use straight-line distance/direction math, not real
    pathfinding — same limitation `EnemyAI` already has everywhere
    (see item 5 further down this list), just more visible here since
    there's more movement happening.
  - **Arcane Shield's cooldown was never specified in the design — I
    picked 30s as a placeholder.** Flag if that's not right;
    `EnemyAI.arcaneShieldCooldown` on the Mage prefab is the field to
    change.

## 7. Aura spells are now always-on (no cast, no keybind)

- **Aura of Replenishment, Aura of Regeneration, and Echolocation no
  longer need to be cast or bound to a key.** You still pick them into
  a slot in Choose Skills like any other ability, but as soon as one is
  in your kit it's continuously active for as long as it stays there —
  no mana cost, no cooldown, nothing to press. You can also now slot
  more than one at once (e.g. Replenishment AND Echolocation together)
  and both stay active simultaneously — previously only one cast-aura
  could be active at a time.
- **This is a real behavior change from before**: previously you had to
  cast an aura spell to turn it on, and casting a different one
  replaced whatever was active. Now, simply having it slotted is what
  turns it on, and removing it from your kit (via Choose Skills) is
  what turns it off. If you already have an aura ability in a saved
  loadout, it should just start working the next time your gear/loadout
  syncs (menu close) with no other action needed.
- **Please confirm**: slot Aura of Replenishment and close the menu -
  mana regen should kick in immediately with no cast. Try slotting a
  second aura (e.g. Echolocation) at the same time and confirm mobs
  show up on your minimap AND you're still getting the mana regen.
  Remove one from your kit and confirm it stops applying. Also confirm
  the Choose Skills panel shows "Always On" instead of a key for these,
  and that there's no "Set Key Binding" button for them anymore.
- **Please also confirm no double-dipping**: have two players both slot
  the same aura (e.g. both run Aura of Regeneration) and stand near a
  third player - that third player should get the heal-over-time from
  ONE source, not doubled, even with two casters pulsing it at once.
  This should already hold (every aura pulse, item or spell, applies
  its effect as a single shared instance per target regardless of who's
  pulsing it), but it's untested with this new always-on behavior.

## 8. Server-wide item uniqueness (new mechanic)

- **Each item Id can now only be equipped by one connected player at a
  time** (`CharacterEquipment.globalItemOwners`). If you and another
  player both try to equip the same item, whoever's gear sync reaches
  the server first gets it — the other's equip attempt is silently
  dropped, exactly like putting an item in the wrong slot already was.
  **Please confirm**: have two players equip the same item at once
  (open Gear, equip it, close the menu) — the first should end up
  wearing it, the second should end up with that slot empty (or
  whatever was there before). Then have the first player unequip it (or
  disconnect) and confirm the second player can now equip it
  successfully.
- **Known, deliberate gap, not fixed**: there's no client-side signal
  that an item is already taken — the Gear menu doesn't grey it out or
  warn you, and if you lose the race your menu keeps showing it
  equipped locally until you reopen the Gear page (which re-syncs from
  the server's actual state). Worth flagging if this is confusing in
  practice rather than something to silently work around.

## 9. Mana orb mob drops (NEEDS AN EDITOR STEP BEFORE IT WORKS AT ALL)

- **Every mob now has a flat 5% chance to drop a mana orb on death**
  (`EnemyAI.HandleDeath` → `ManaOrb.TrySpawn`), which restores 250 mana
  to whichever player touches it, then disappears. **This one needs an
  Editor step before it does anything at all**: the pickup prefab has
  to live at exactly `Assets/Resources/Prefabs/ManaOrb.prefab` (loaded
  by path, not wired to a field, so every mob picks it up automatically
  with no per-prefab setup) — see "Not yet done" #8 in `CLAUDE.md` for
  the exact steps. Until that prefab exists, the drop roll still
  happens but silently no-ops (a console warning, no orb).
- **Please confirm once the prefab is in place**: kill enough mobs to
  see a drop (5% is roughly 1 in 20, so summon and clear a batch rather
  than expecting one from a single kill), walk over it as a player, and
  check your mana bar jumps by 250 (capped at your max - if you're
  already above 750 mana with 1000 max, it'll cap rather than overfill).
  Confirm it disappears after being picked up once (not still there to
  double-dip), and confirm a *mob* walking over it does nothing (only
  `PlayerMovement`-having characters trigger it).

## 10. Soul Siphon (new mechanic: tick lifesteal)

- **Soul Siphon** (new spell, unit-targeted, instant, 250 mana, 3s
  cooldown): 50 damage every 3s for 18s (6 ticks total), and each tick
  also heals YOU (the caster) for 10% of whatever that tick actually
  dealt after the target's armor mitigation - not a flat 5 every tick,
  it'll be less against an armored target. **This needed a small
  architecture change**: `CharacterStats.DealDamage` used to return
  nothing, so there was no way for a DoT tick to know how much damage
  it actually dealt in order to heal a *third party* (the caster, not
  the target) off of it - it now returns the mitigated amount, and only
  this one new code path (`TickEffect`'s lifesteal branch) uses the
  return value; every other call site is unaffected. **Please confirm**:
  cast it on a mob, watch your own health tick up every 3 seconds while
  the mob's health ticks down, and sanity-check the heal amount is
  roughly 10% of the damage that actually landed (armor-adjusted, not a
  flat 5). Also confirm your own `HealingMultiplier` gear (e.g. Holy
  Scepter) boosts the lifesteal heal too, same as any other heal you
  perform - and that it generates healing threat like normal healing
  does, which could be an unexpected side effect worth flagging if you
  didn't want a *damage* spell also generating extra threat via its
  self-heal.

## 11. Armor floored at 0

- **Armor can no longer go negative** - `Stat` gained an optional
  `minValue` (defaults to no floor for every stat except Armor, which
  now floors at 0). Covered by new EditMode tests
  (`StatTests.MinValueClampsBelowIt`/`MinValueDoesNotAffectValuesAboveIt`),
  low risk, but worth a quick sanity check: get hit by Armorbreaker 5+
  times (or otherwise stack enough Armor-reducing debuffs to exceed your
  base Armor) and confirm damage taken doesn't start exceeding 100% of
  the raw hit - it should just floor out at "no mitigation," never
  flip into bonus damage.

## 12. Armorbreaker (new mechanic: stacking effects)

- **Armorbreaker** (new 1H MainHand weapon, 40 dmg / 2s swing): on hit,
  applies a stacking armor-reduction debuff - each hit adds a stack
  (−2 Armor per stack) up to 5 stacks (−10 Armor total at max), 10
  seconds duration, shared across all current stacks (i.e. one timer for
  the whole debuff, not a separate 10s per stack - getting hit again
  resets the timer for everything currently stacked, it doesn't extend
  or stack the *duration*, only the *magnitude*, up to the cap).
  **This needed a genuinely new mechanic** (`EffectStackingMode
  .StackUpToLimit`) - nothing like "reapplication increases magnitude,
  capped, one shared timer" existed before this. **Please confirm**:
  equip it, auto-attack (or manually trigger hits on) a mob or dummy
  target repeatedly, and watch their effective Armor drop by 2 per hit
  up to 5 hits (check via how much less your other attacks are
  mitigated, or however Armor is otherwise visible) - then confirm it
  caps at −10 and doesn't keep growing past hit 5. Stop attacking and
  wait past 10s - confirm the WHOLE debuff (all stacks) clears at once,
  not one stack at a time. This is the riskiest part of this batch since
  it touches the shared `StatusEffectTracker`/`CharacterStats` effect
  plumbing that every other buff/debuff in the game also goes through -
  worth also re-confirming a couple of the OLDER effects (e.g. Burn,
  Slow) still behave normally afterward, in case something in this
  change subtly affected the shared code path.

## 13. Belt slot removed

- **The Belt gear slot no longer exists** (it was always empty - nothing
  ever occupied it, so no item needed reassigning). The Gear menu's
  paper-doll grid should now show 12 slots instead of 13 - **please
  confirm it just quietly has one fewer box** (Legs should sit right
  after Gloves now) rather than leaving a blank gap or shifting
  something into the wrong labeled slot. Every OTHER slot's underlying
  numeric value was deliberately kept identical to before (Legs is
  explicitly pinned in the enum) specifically so this wouldn't disturb
  any existing item's slot assignment - worth a quick check that Boots,
  Rings, Trinket, Main Hand, and Off Hand items still equip into the
  right places, same as the last slot-renumbering check on this list.

## 14. Aegis of the Ancient (block chance), Aegis of Reflection (damage reflect), Shield renamed

- **Block chance is now additive, not a flat set** (per your follow-up):
  both shields (Aegis of the Unstoppable, Aegis of Reflection) passively
  give +5% block chance just from being equipped, and Aegis of the
  Ancient adds +25% on top of that for its 12s duration - so with a
  shield worn and the buff active, total block chance is 30%, not 25%.
  This is implemented as a proper stat now (`StatType.BlockChancePercent`)
  that gear and effects both just add `Flat` modifiers to, same as every
  other stat in the game - no more special-casing. **Please confirm**:
  with a shield equipped and the buff NOT active, roughly 1 in 20 melee
  hits should block (5%); with the buff active, roughly 3 in 10 should
  (30%). Also confirm the 5% passive block works even without ever
  casting the spell (just from wearing a shield).
- **Aegis of the Ancient** (new mechanic, `[requires a shield]`) - equip
  a shield (Aegis of the Unstoppable or Aegis of Reflection, both now
  count), cast it, then have something melee you repeatedly for the
  next 12 seconds. Confirm it does NOT block spell/ability damage (e.g.
  a mob or player's Firebolt should never be blocked - only
  melee/basic-attack-sourced hits count as "physical" here, that's an
  interpretation call since "physical" wasn't defined further). Confirm
  casting it WITHOUT a shield equipped is rejected ("Requires a
  shield"). 100 mana / 60s cooldown / 12s duration / +25% came from you;
  instant cast and range are placeholders (it's self-only, no target
  needed).
- **Aegis of Reflection** (new item, new mechanic: `StatType
  .DamageReflectPercent`) - +10 Armor, and now reflects 3% of incoming
  damage back at whoever hit you. **This needed a small architecture
  change to actually work against mobs**: the existing damage-attacker
  lookup only ever resolves *players* (via clientId), so reflecting
  damage back at a melee-swinging mob required adding a direct
  `CharacterStats` reference to `HitInfo` (`EnemyAI`'s melee attack is
  the only place that sets it so far). **Please confirm**: equip it,
  let a mob hit you a few times, and check the mob's own health bar
  ticks down slightly each time (3% of what you took) - this is the
  part most likely to have a subtle bug since it's new plumbing, not
  just a new number on an existing system. Also confirm it doesn't
  cause any weirdness if you and another reflection-wearer somehow hit
  each other (shouldn't happen in normal play, but the reflected hit
  itself is coded to never itself be reflected again, to avoid a loop -
  never actually tested).
- **The old "Shield" item is now "Aegis of the Unstoppable"** - same
  +20 Armor, same behavior, purely a rename (Id changed too:
  `shield` → `aegis_of_the_unstoppable`). If you had it equipped in a
  saved profile, expect it to show as unequipped once (re-equip from
  the new name) - same as every other item rename this session.

## 15. Arctic Winds (NEEDS AN EDITOR STEP BEFORE IT WORKS AT ALL)

- **Arctic Winds cannot be tested yet** - same situation as Earthen
  Bastion below: the gameplay code is written and compiles, but there's
  no zone prefab assigned, so casting it will currently just fizzle
  with "Zone not configured yet". **Before testing, do this in the
  Editor**:
  1. Create a GameObject (a Sphere if you want the dome visible, or an
     empty GameObject for an invisible trigger - your call, wasn't
     specified).
  2. Add a `NetworkObject` component to it.
  3. Add the `FollowingZone` component (`Scripts/Abilities/
     FollowingZone.cs`) - it'll auto-add a `SphereCollider`; its radius
     gets set automatically at cast time, no scaling needed.
  4. Drag it into the Project window to make it a prefab.
  5. Register it in `DefaultNetworkPrefabs.asset`.
  6. Open `AbilityArcticWinds` (`Assets/Resources/Data/Abilities/`) and
     drag the new prefab onto its `Following Zone Prefab` field.
  - Same reason as Earthen Bastion's prefab - a `NetworkObject` needs
    the Editor to compute its `GlobalObjectIdHash` correctly, so I
    couldn't safely hand-write this one either.
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

## 16. Healing now generates threat (new mechanic)

- **All healing spells now generate 15% of the healed amount as threat**
  (instant heals AND each HoT tick - Radiant Embrace, Blessing of
  Vitality, Everliving Touch, Seraph's Grace, and any future healing
  spell automatically, since it's implemented once inside
  `CharacterStats.Heal` rather than per-ability). Please confirm: heal a
  damaged ally who's currently being attacked by a mob (mob has some
  existing threat against them), and check that mob's targeting shifts
  toward the healer over repeated heals, same as it would from the
  healer dealing direct damage. **Also confirm the aura spells (Aura of
  Regeneration) do NOT generate threat** - this was a deliberate scope
  decision (aura-pulsed healing was already excluded from the
  HealingMultiplier bonus for the same reason, before this change even
  existed), not something explicitly re-confirmed with you, so flag it
  if you actually wanted the aura to generate threat too.
- Also worth confirming healing a mob that ISN'T currently in combat
  with anything (no mob has threat against them yet) doesn't do anything
  weird - it should just silently generate zero threat (nothing to add
  it to), not error.

## 17. Earthen Bastion (NEEDS AN EDITOR STEP BEFORE IT WORKS AT ALL), Team Up redirect change

- **Earthen Bastion cannot be tested yet** - all the gameplay code is
  written and compiles, but there's no wall prefab assigned, so casting
  it will currently just fizzle with "Structure not configured yet".
  **Before testing, do this in the Editor**:
  1. Create a Cube GameObject.
  2. Add a `NetworkObject` component to it.
  3. Add the `PlacedStructure` component (`Scripts/Abilities/
     PlacedStructure.cs`) - it'll auto-add a `BoxCollider` too (leave it
     as a non-trigger, that's what makes it actually block movement).
  4. Drag it into the Project window to make it a prefab.
  5. Register it in `DefaultNetworkPrefabs.asset` (same list Firebolt's
     projectile/patches are already in).
  6. Open `AbilityEarthenBastion` (`Assets/Resources/Data/Abilities/`)
     and drag the new prefab onto its `Structure Prefab` field.
  - I couldn't do this part myself - a prefab with a `NetworkObject`
    needs the Editor to actually compute/serialize its
    `GlobalObjectIdHash` correctly, unlike a plain ScriptableObject
    `.asset` (which is why every OTHER new spell/item this session was
    safe to hand-write, and this one piece isn't) - same category of
    problem as the Decal Projector renderer-feature step that's been on
    this list for a while.
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
- **Team Up changed** (per your explicit follow-up): the target no
  longer gets "−10% damage taken" for 15s - now it's a full damage
  **redirect** to the Team Up caster for 3s (reuses the exact same
  mechanism One For All already uses, just 100% instead of 10%, and 3s
  instead of 30 minutes). Confirm: charge to an ally, have them get hit
  by a mob for the next 3 seconds, and watch YOUR health drop instead of
  theirs (their health should not move at all during that window, other
  than any of their own effects).

## 18. Hunter's Bow (first ranged basic attack), Staff now two-handed

- **`WeaponData.Range` is now per-weapon** instead of one shared
  constant for everyone (`WeaponData.BasicAttackRange` still exists as
  the fallback default) - this touched `PlayerAutoAttack` and `EnemyAI`,
  so **please re-confirm basic melee auto-attack still works exactly as
  before** for Fists/Broad Sword/2H Axe/goblins/ogres (all explicitly
  set to `Range: 2`, matching the old constant) - low risk since it's
  the same number, but it's a shared-code-path change.
- **Hunter's Bow** (new item, MainHand, 60 dmg / 2.5s swing, **50
  range** - the first weapon whose basic attack reaches farther than
  melee): right-click a distant target and confirm auto-attack actually
  fires from range without needing to walk into melee distance first,
  and that it correctly stops firing/needs re-arming if the target
  moves beyond 50 units. **Not marked two-handed** - wasn't requested,
  even though bows conventionally are two-handed - flag if that's
  actually wanted.
- **Staff is now two-handed** (`ItemData.TwoHanded: true`) - equipping
  it should now auto-clear whatever's in Off Hand (e.g. Shield), same
  mutual-exclusivity behavior the 2H Axe already has. Confirm a
  previously-saved profile with both Staff and an Off Hand item
  equipped resolves cleanly (Off Hand empties) the next time gear syncs.

## 19. Boots of Lightness (new air-hover mechanic), Armored Boots, Amulet of Vitality +5 Armor

- **Boots of Lightness (genuinely new movement mechanic, never run)**:
  equip them, jump, then press Jump again while still in the air.
  **Confirm**: you stop falling (gravity suspended) for ~2 seconds, and
  WASD freely steers you around the whole time - not stationary, you
  can glide/fly wherever you point (this was the specific ask - a
  normal jump/fall locks in your horizontal direction at launch,
  hovering should NOT), and gravity resumes normally once the 2s runs
  out or you land. **Confirm it's once per airtime** - pressing
  Jump again mid-hover, or after the hover ends but before you land,
  should NOT grant a second hover; only landing (touching ground again)
  should re-arm it. This is fully client-local (owner-authoritative
  movement, same trust model as the rest of `PlayerMovement`) - no
  server round trip, so also worth confirming a remote (non-host) client
  can actually do this without getting snapped back by the movement
  validator (it shouldn't - hovering only affects vertical velocity, and
  the validator only polices horizontal speed and below-ground, so this
  should be a non-issue, but it's never been run against a real second
  client).
- **Armored Boots** (+5 Armor) - trivial, low risk.
- **Amulet of Vitality now also gives +5 Armor** on top of its existing
  +150 Max Health - confirm both bonuses actually apply together, not
  just whichever was added first.
- **Amulet of the Berserker** (+5 Weapon Damage, new `StatType
  .WeaponDamageBonus` stat) - confirm basic auto-attack damage goes up
  by exactly 5 while equipped, AND that weapon-scaling abilities
  (Reaper's Wheel, Cleave at 100%; Crippling Blow at 25%, Seismic Slam
  at 10%) reflect the bonus proportionally to their percentage (e.g.
  Crippling Blow should only gain ~1.25 damage from it, not the full 5) -
  this proportional-scaling behavior was a design choice, not something
  explicitly specified, so flag if a flat +5 everywhere was actually
  wanted instead.

## 20. Crippling Blow / Seismic Slam weapon-scaled damage, Trample retune, Tomb of the Magi, The Everflow

- **`AbilityData.WeaponDamagePercent`** replaced the old all-or-nothing
  `UseWeaponDamage` bool (a straight rename/generalization, same field
  slot in the YAML - existing Reaper's Wheel/Cleave behavior is
  unchanged, still 100% weapon damage). New: **Crippling Blow** now also
  deals 25% weapon damage (previously 0, slow-only) and **Seismic Slam**
  now also deals 10% weapon damage (previously 0, stun-only) - both on
  top of their existing effects. Confirm both actually show nonzero
  damage numbers now, scaling with whatever's in your Main Hand (Fists
  excluded - both still require a real melee weapon to cast at all).
- **Trample retuned**: damage 75 → 30, cooldown 15s → 30s (a
  significant nerf - confirm it still feels worth using at the new
  numbers).
- **Tomb of the Magi** (Off Hand, +50 max mana, +0.4 mana/sec = +2 per
  5s tick) and **The Everflow** (Trinket, +1 mana/sec = +5 per 5s tick) -
  both plain stat items, lowest risk, never equipped/tested.

## 21. Global cooldown, Aura spells (replacing the amulets + Echolocator), Cleanse, 2 new amulets

- **Global cooldown (explicitly requested)**: casting ANY ability now
  locks out starting a different one for 1.5s (not specified, the
  standard MMO GCD length - flagged placeholder), independent of that
  ability's own cooldown. **Please confirm**: cast one spell, and
  immediately try a completely different one on a different key within
  ~1.5s - it should show "Global cooldown" and do nothing, then work
  normally once the GCD passes. Also confirm it does NOT block re-use of
  the SAME ability once ITS OWN (possibly longer) cooldown is up - the
  GCD should never be the binding constraint for a slow ability, only
  for chaining two different fast/instant ones back to back. Never run.
- **Aura spells (new mechanic, replacing 3 gear items)**: the two
  amulets and Echolocator no longer exist as items - equipping/gear
  pickers should show them gone entirely. In their place: **Aura of
  Replenishment**, **Aura of Regeneration**, **Echolocation** - cast any
  one (instant, 50 mana, 0 cd - all placeholders, not specified), and its
  effect (mana pulse / heal pulse / mob-reveal) should turn on
  permanently, with **no gear required and no expiry**. Confirm the
  pulse still only reaches allies within 40 units (same as the old
  amulets) and includes yourself. **Confirm exclusivity**: cast Aura of
  Replenishment, then cast Aura of Regeneration - Replenishment's mana
  pulse should stop the instant Regeneration's heal pulse starts (only
  one active at a time, per your explicit answer to a clarifying
  question - not a guess). Also confirm Echolocation slotted in as one
  of the three behaves the same way (casting it turns off whichever
  aura was previously active, and vice versa). None of this has ever
  been run - it's a genuinely new mechanic (permanent, gear-less,
  spell-granted auras) with no precedent to lean on.
- **Cleanse** (new mechanic): instant, 150 mana, removes one active
  negative effect from the target (e.g. Burning, Slowed, Bleeding,
  Stunned, Crippled) - never removes a buff/aura. If the target has more
  than one debuff active, whichever gets picked is arbitrary (not
  specified which should take priority) - confirm it at least always
  removes something when a debuff is present, and does nothing/fizzles
  usefully when there isn't one. Range 30 and 6s cooldown are guesses.
- **Amulet of Vitality** (+150 max health) and **Amulet of the Magi**
  (+100 max mana, +0.4 mana/sec = +2 per 5s tick) - both plain
  stat-bonus Necklace items, lowest risk in this batch, but still never
  equipped/tested.

## 22. Broad Sword threat, 2H Axe, two-handed/off-hand exclusivity

- **Broad Sword**: threat-generation bonus changed from +20% to +40%
  (`ThreatMultiplier`) - straightforward number change, low risk.
- **2H Axe** (new item, `two_handed_axe`, MainHand, `TwoHanded: true`,
  80 weapon damage, 3s swing interval - set by the user 2026-09-12, no
  longer a guess). No other bonuses were requested, so it has none (no
  threat/HP/armor bonus like Broad Sword has) - confirm that's intended,
  not an oversight.
- **Two-handed/off-hand mutual exclusivity** (new mechanic,
  `ItemData.TwoHanded`): equipping the 2H Axe into MainHand auto-clears
  whatever's in OffHand (e.g. unequips Shield), and equipping something
  into OffHand while the 2H Axe is equipped auto-clears the 2H Axe from
  MainHand instead - handled both in the gear menu (`MainMenu.cs`, for
  instant visual feedback) and authoritatively in
  `CharacterEquipment.SetGearServerRpc` (a hard backstop - MainHand is
  always processed before OffHand in the slot loop, so OffHand is what
  gets silently rejected server-side if the two ever conflict, no matter
  what the client sent). **Please confirm**: equip 2H Axe → Shield slot
  visibly empties; equip Shield with 2H Axe on → 2H Axe slot visibly
  empties (and your auto-attack should fall back toward Fists/whatever
  else is in MainHand, not silently keep swinging for 80). Also confirm
  a saved profile that somehow has both isn't possible to reach through
  the menu at all.

## 23. Seven new warrior spells/item (Reaper's Wheel, Trample, Cleave, Team
   Up, Crippling Blow, Seismic Slam, Barbarian's Mantle) — biggest and
   most novel batch yet, several brand-new mechanics

This batch introduced multiple mechanics that never existed before, so
treat all of it as unverified:

- **Stun (new mechanic)**: `StatusEffectData.IsStun`, checked via
  `CharacterStats.IsStunned`. Currently only `EnemyAI` obeys it (mobs
  hit by Trample/Seismic Slam freeze in place, no movement/attacks, for
  the effect's duration). **Players are NOT gated by stun** - nothing in
  this batch stuns a player, so `PlayerMovement`/`PlayerAbilities` were
  deliberately left unchanged. If a future ability stuns a player, this
  will need adding there too.
- **"Enemies" = anything without a `PlayerMovement` component** (mirrors
  the existing "allies" = has `PlayerMovement` filter `AreaAroundCaster`
  already used, just inverted). Reaper's Wheel/Seismic Slam
  (`EnemiesAroundCaster`) and Cleave (`ConeAroundCaster`) all use this.
  Confirm they hit mobs but never other players.
- **Assumed AoE radius: 8 units** for Reaper's Wheel, Cleave, and Seismic
  Slam's "around you" reach - not specified in the request; still just a
  placeholder (`AbilityData.GroundEffectRadius`) - trivial to retune.
  Cleave's cone angle was set by the user directly: **120°** (matches
  the facing cone other abilities already use).
- **"[require melee weapon]" was interpreted as a hard cast-blocking
  gate**: `AbilityData.RequiresMeleeWeapon` fizzles/rejects the cast if
  `CharacterEquipment.MainHandWeapon` is null (fists don't count),
  independent of whether the ability also scales off weapon damage.
  Applied to Reaper's Wheel, Cleave, Crippling Blow, Seismic Slam (all 4
  tagged abilities) - NOT to Trample or Team Up (untagged). Confirm
  casting any of the 4 unarmed shows "Requires a melee weapon" and does
  nothing.
- **"Weapon damage" scaling** (Reaper's Wheel, Cleave): reads whatever
  the caster's auto-attack would currently swing with
  (`PlayerAutoAttack.ResolvedWeapon` - equipped main hand, or Fists) at
  the moment the ability resolves. Confirm swapping weapons changes the
  ability's damage accordingly.
- **Trample (new self-charge mechanic)**: caster dashes forward 10 units
  in whatever direction they're currently facing (no target needed),
  hitting/stunning any enemy within 2.5 units of that straight-line path
  (a one-time resolve-time check, not continuous collision - an enemy
  that darts into the path mid-charge won't be caught). Charge speed
  assumed **20 units/sec** (not specified) - reuses the existing
  Vacuum/Force-pull rail (`PlayerMovement.ServerBeginPull`) under the
  hood, so it should feel like getting pulled, just self-initiated.
  Confirm: no target required, moves the caster (not the mobs), damage +
  3s stun lands on everything roughly in the path, and it doesn't let
  you charge through walls/off the map (no obstruction check exists -
  flag if that matters).
- **Team Up (new gap-closer + ally buff)**: interpreted as a *support*
  ability - `RequiresTarget` (an ally, like the healing spells), the
  caster charges to just short of the target (2-unit clearance) using
  the same charge rail as Trample, then the target (not the caster)
  gets "reduced damage taken" for 15s. This needed a brand new stat,
  `StatType.DamageTakenMultiplier` (applied in `CharacterStats.DealDamage`
  right after armor mitigation) - also reused by Barbarian's Mantle
  below. Assumed **cast range 25** (how far away you can initiate the
  charge) - not specified. Confirm: charging to a full-health ally
  actually reduces damage they take by 10% for 15s, and the caster
  visibly moves to them.
- **Crippling Blow**: no new mechanic - same pattern as Icebolt's Slow
  (a `RunSpeed` `PercentAdditive` -50% modifier, 10s), just standalone
  and melee-gated. Lowest-risk item in this batch.
- **Barbarian's Mantle (new HP-threshold-conditional item mechanic)**:
  new `ItemData.HpThresholdEffects` list, evaluated once/sec in
  `CharacterEquipment` (same cadence as auras) - above 50% HP applies
  -15% `DamageTakenMultiplier`, below 50% applies +15% `DamageMultiplier`,
  swapping automatically as health crosses the line (only re-applies
  modifiers on an actual crossing, not every tick). **Assumed slot:
  Chest** ("(armor)" read as body armor, not the `Cape` slot despite the
  "mantle" name) - easy one-line change if you want it in `Cape`
  instead. Confirm: damage taken/dealt actually changes right as you
  cross 50% HP in either direction, and unequipping it cleanly removes
  whichever side was active.
- None of these 7 have `CastVfxPrefab`/`ProjectilePrefab` assigned (no
  visual assets were provided) - they'll cast with no cast-bar VFX,
  matching how earlier spells started out.

## 24. Five new healing spells + shields + Holy Scepter — biggest untested batch yet

- **Radiant Embrace**: cast on a hurt ally, confirm exactly 350 healing
  lands (or 385 with Holy Scepter's +10%).
- **Blessing of Vitality**: confirm the heal lands AND the target gets
  "Vitality Ward" for 16s, AND that while it's up they take noticeably
  less damage (10% less — e.g. a 100-damage hit should land for ~90,
  modulo their own armor). This is implemented as a flat +10 Armor
  buff, reusing the same formula Shield the item already uses — should
  just work, but never seen combined with a target's own existing armor.
- **Aegis of Arcane**: shield an ally for 350, then have them take
  damage — confirm damage is absorbed first (health doesn't drop until
  350 has been eaten), the HUD shows a cyan "Shield: N" number counting
  down, and once it hits 0 further damage reduces health normally.
  Confirm a shielded hit does NOT also get redirected if the shielded
  player also has One For All active on them (shield should apply
  first, so redirect only sees what's left after the shield). Also
  confirm shielding someone who already has a shield **replaces** the
  old amount rather than adding to it (intentional design choice, not
  a bug).
- **Everliving Touch**: 600 total healing over 18s in 100/3s ticks.
  Have two different players cast it on the same target and confirm
  both HoTs tick independently (600 total heal doubles up), same as
  Amulet of Regeneration's aura already should.
- **Seraph's Grace**: stand near 2+ other players, cast it, confirm
  everyone within 50 units (including yourself) heals for 350
  simultaneously, and nobody outside 50 units does. This is a brand
  new "no targeting at all, just hits everyone near you" cast path
  (`AreaAroundCaster`) — never exercised before.
- **Holy Scepter**: equip it and confirm ALL of the above heals (and
  Amulet of Rejuvenation-style ticks) increase by 10% — but confirm the
  Mana/Regen amulets' *aura* healing does NOT get the bonus (deliberate:
  the scepter boosts cast spells, not passive gear auras).
- All 5 abilities' `Range: 30` (where applicable) was never specified —
  only the numbers listed above came from you.

## 25. Player 2 falling through the map on spawn — fix applied, needs confirming

- This was diagnosed from reading the code, not from being able to
  reproduce it — the movement validator had no grace period after spawn,
  so a remote (non-host) player's position could get judged before their
  own terrain-snap had round-tripped back to the server, and "corrected"
  right back into the ground. Fixed in `PlayerMovement`
  (`spawnGraceSeconds`) and `PlayerRespawn` (death-respawn now pre-arms
  the validator via `ServerTeleportTo` instead of its own RPC).
- **Please confirm**: connect as a second (non-host) client and watch
  them spawn — do they land and stand normally? Die and respawn a few
  times too, since that path changed as well.
- If it still happens, check the console for `[PlayerMovement] client
  ... movement rejected (BelowGround)` right around spawn/respawn time —
  that would mean the fix didn't fully close the window (e.g.
  `spawnGraceSeconds` isn't long enough over real network latency).

## 26. Mana-on-successful-cast, and mob dots always red

- Both were straightforward, targeted changes — lower risk than the rest
  of this list, but still never actually run.
- Cast Firebolt/Icebolt (2s cast time) and watch your mana bar — it
  should stay full until the cast *completes*, then drop, not drop the
  instant you start casting. Let a cast fizzle on purpose (e.g. target
  walks out of range mid-cast) and confirm no mana was spent.
- Minimap: with Echolocator equipped and a mob currently targeted, its
  pulsed dot should stay plain red like every other mob, never turning
  yellow the way it used to.

## 27. Force Compression / Force Expansion / ground-targeted casting (highest risk — newest, most moving parts)

**Confirmed working locally (single-client/editor test). Still needs a
retest against the dedicated server** — the movement-validator
suppression during forced movement is server-authoritative and involves
real network latency, which local testing doesn't exercise.

- Press either spell's hotkey: does a ring reticle appear and follow the
  mouse across the terrain? Does it correctly ignore player/mob
  colliders (aim "through" a mob standing in front of the spot) and only
  hit ground?
- Left-click within range: does it cast there? Outside range: does it
  show "Out of range" and stay in aiming mode rather than canceling?
- Press the same hotkey again while aiming: does it cancel cleanly (no
  reticle left behind)?
- Cast **Force Compression** near a mix of players and mobs standing
  within the 30-unit circle — do all of them get pulled toward the
  center, including the caster if they're standing in the circle too?
- Cast **Force Expansion** the same way — does everyone in the circle
  get blasted radially *outward*, each in their own correct direction
  away from the center (not all flung the same direction)? Do they stop
  a bit past the edge of the circle rather than flying forever?
- **Watch the console during either spell for `[PlayerMovement] client
  ... movement rejected` warnings.** If any appear during/right after a
  cast, the movement-validator suppression isn't covering the full
  window and a moved remote player will get snapped back mid-flight —
  this is the part most likely to be subtly wrong since it couldn't be
  tested at all.
- Does an affected player regain normal WASD control immediately once
  the forced movement ends (not stuck, not a weird pause)?
- Placeholder numbers that were never specified, only guessed — the
  same two on both spells since they're otherwise identical: Range 30,
  Force speed 15/s. The 30-unit diameter (→ 15-unit radius), instant
  cast (0s), 25s cooldown, and 240 mana cost all came from you.

## 28. Recall

- Target a player or a mob and cast Recall — does it teleport instantly
  to right where you're standing?
- **Watch the console for `[PlayerMovement] client ... movement
  rejected` right after a Recall on a remote player.** If it appears,
  the validator re-baseline in `ServerTeleportTo` isn't landing before
  the client's next replicated state does, and the recalled player will
  get snapped back to where they were. Same class of risk as the pull
  spells, never actually run.
- Since characters pass through each other now, teleporting the target
  onto your exact position shouldn't cause any visible clipping/pushing
  — worth a glance to confirm.
- Cooldown (30s), mana cost (200), and range (40) came from you;
  instant cast and no threat generated are still unlabeled guesses.

## 29. One For All (damage redirect) — genuinely new mechanic, unrun

- Cast it on an ally, have a mob hit *them*, and confirm 10% of that
  damage lands on *you* (the caster) instead — watch both health bars.
  The other 90% should still hit the ally normally.
- Confirm the redirected 10% doesn't get re-armored on your end (it's a
  direct siphon of the already-mitigated amount, not a fresh "hit"
  against your own armor) — the math to check: if the ally takes X
  damage after their own armor, you should lose exactly 0.1×X, not
  something further reduced by your armor too.
- If the redirect kills *you* (caster) rather than the ally, confirm you
  actually die (OnDeath fires) — this was implemented via a shared
  death-check helper but never seen run.
- Cast it on player A, then recast it on player B (different target) —
  does A's buff actually go away (check their buff/effects display),
  and does B now have it instead? Recast on B again (same current
  holder) — should just refresh/extend, not do anything weird.
- **Now handled deliberately** (was an unhandled edge case, fixed the
  same day): two different casters each casting their own One For All
  on the *same* third player. Caster A bonds the target first; caster B
  then casts it on the same target — B should cleanly take over the
  bond (target's redirect now points to B, not A), not conflict or
  silently corrupt state. This is the new `EffectStackingMode.Override`
  behavior (see CLAUDE.md) — covered by EditMode tests
  (`OverrideModeAlwaysWinsEvenWhenShorterAndReattributes`) but never
  seen running against a real second player.
- Separately, confirm a **heal-over-time actually stacks** when two
  different players each apply one to the same target — e.g. two people
  wearing Amulet of Regeneration standing near the same ally should both
  tick their own heal independently (double healing), not fight over a
  single shared instance. This is `EffectStackingMode.StackPerCaster`
  (`EffectRejuvenation`), also covered by tests
  (`StackPerCasterGivesEachCasterTheirOwnInstance`) but never run live.
  Known cosmetic gap either way: the on-screen buff timer only ever
  shows **one** entry even when two instances are ticking (see CLAUDE.md
  "Known, deliberate limitation").
- Cooldown (5s) and Range (30) are unconfirmed guesses; duration (30
  min), redirect (10%), mana cost (50), and instant cast all came from
  you.

## 30. Auto-attack

- Right-click a mob (a quick click, not a drag): does it target and arm
  auto-attack without also turning the camera?
- Does the swing only start once actually within melee range, and does
  walking up to a distant target trigger it correctly on arrival?
- Tab to a different target while auto-attacking — does it follow, or
  get stuck attacking the old target?

## 31. Party frames + F1–F5 targeting + minimap compass

- With 2+ clients connected: does each player see the *other* player(s)'
  health/mana bars top-right, correctly updating live as they take
  damage/cast spells?
- Do all clients agree on each player's label (e.g. does everyone see
  the same person labeled "Player 3", regardless of whose screen it's
  on)? This should hold automatically since labels come from the
  canonical `OwnerClientId` sort, not the viewer-filtered row position —
  worth eyeballing with 3+ players specifically because those two only
  diverge once someone in the middle of the order gets skipped.
- Confirm nobody ever sees a frame for themselves.
- With only 1 player connected, confirm nothing draws (no empty box).
- **F1–F5**: with 3+ players connected, confirm each key targets
  whoever's actually drawn in that row on *your own* screen — per the
  design this can be a different canonical player than the key number
  suggests once you've skipped yourself (e.g. your F2 might target
  "Player 3"). This is the part most likely to be subtly wrong since it
  was never run.
- **Known, deliberately unresolved conflict**: F1–F5 are also selectable
  ability hotkeys in the Options/loadout menu. If you'd bound an ability
  to F2, pressing it now does both — fires the ability *and* retargets.
  Worth deciding whether to remove F1–F5 from the ability-keybind pool,
  or leave it.
- **Compass**: do N/E/S/W actually line up with real N/E/S/W (i.e. does
  walking toward the "N" label increase world Z)? The math should be
  right but was never seen rendered.

## 32. Minimap / Echolocator

- Confirm the map is blank with no items equipped (just your own dot).
- Equip Echolocator: do mobs pulse onto the map every ~5s, stay frozen
  (not tracking), and fade out over ~4s before the next pulse (leaving
  a ~1s gap with nothing)?
- Transmitting Beacon on another player: do they show as a green dot on
  your map even with no reveal gear of your own equipped?

## 33. Gear — ring slots and the recent renumbering

- Equip a ring (Transmitting Beacon): does it go into Ring 1, and if
  Ring 1 is already full, does a second ring correctly fall into Ring 2
  instead of failing?
- Check Main Hand / Off Hand / Trinket items (Broad Sword, Staff,
  Shield, Ember Stone) still equip into the right slot — their slot
  index shifted when Ring3/Ring4 were removed and I renumbered them by
  hand.
- If you had gear equipped in a saved profile from before that change,
  expect Main/Off/Trinket to have reset to empty — known, not a bug,
  just re-equip once.

## 34. General combat/economy numbers worth a sanity pass

- Mana: 120 per bolt, 1000 pool (1250 with Staff's new +250 max mana),
  regen in 5s ticks — does an actual fight feel like mana is a real
  constraint now, or too tight/loose?
- Burning: 10 dmg/s for 5s — feels right, or needs another pass?
- Amulet of Replenishment (+15 mana per 5s tick, renamed from Amulet of
  Mana) and Amulet of Regeneration (+10 hp per 5s tick, renamed from
  Amulet of Rejuvenation) — relative strength of the two next to each
  other, and whether the new names read clearly against their effect
  names ("Regeneration" grants the "Rejuvenation" buff — the underlying
  effect wasn't renamed, only the item).

## 35. Firebolt/Icebolt VFX not homing — diagnosed and fixed (twice), needs confirming

- **First fix (real, but not the actual cause)**: the VFX prefabs'
  particle systems had Simulation Space set to World on most layers,
  which was genuinely wrong (fixed - see the moveWithTransform flips),
  but flipping it made no visible difference, because -
- **Actual root cause**: `Projectile_Fire.prefab`/`Projectile_Ice.prefab`
  (under `Assets/External/Spells Pack/...`) each carry a leftover
  Rigidbody + SphereCollider + a demo MonoBehaviour from the asset pack
  itself, which sets `Velocity: {x: -15, y: 0}` on the Rigidbody at
  spawn. Since that Rigidbody was non-kinematic with gravity on,
  Unity's physics engine independently propelled the VFX in a straight
  line from the moment it spawned - completely bypassing the parent
  transform `Projectile.cs` steers every `FixedUpdate`. Two competing
  movement systems on the same object; physics was winning visually,
  which is exactly "flies straight while the tracer curves."
- **Fix applied**: disabled the demo MonoBehaviour and set the
  Rigidbody to kinematic/no-gravity on both prefabs' root objects (also
  disabled their leftover SphereCollider, since an active physics
  collider on a VFX object could cause unwanted hits/pushes). The
  earlier Simulation Space fix now actually matters, since the particles
  are no longer riding on a physics body that ignores the parent.
- **Please confirm**: cast Firebolt/Icebolt at a moving target and watch
  the VFX curve/track toward it the same way the tracer/actual hit does.
- **Separate, unrelated bug surfaced by the console log you pasted, now
  fixed**: `impactVfxPrefab` and `CastVfxPrefab` were both showing
  "Serialized reference type mismatch... expects 'GameObject' but the
  stored reference is a 'Prefab'" - a guessed fileID (100100000, the
  right convention for referencing a whole prefab asset, wrong for
  referencing a specific GameObject inside one) that didn't match the
  real root GameObject of their target prefabs, nulling both at
  runtime. Corrected to the real root fileID, read directly from each
  target prefab file. Please confirm the impact burst and cast-hand-glow
  actually play now.
- **Charging VFX changed to reuse the bolt's own look**: `CastVfxPrefab`
  (played on the caster's hand for the 2s cast time) previously pointed
  at the asset pack's separate `Casting_Fire_4`/`Casting_Ice` prefabs,
  which look different from the actual bolt. Per your request, both
  abilities' `CastVfxPrefab` now point at the exact same
  `Projectile_Fire`/`Projectile_Ice` prefab the traveling bolt itself
  uses - `PlayCastVfxClientRpc` already parents it to the caster and
  destroys it after the cast time, so it reads as "the same fireball/
  icebolt, just sitting stationary in your hand" rather than a
  different effect. Please confirm it looks right and isn't cut off
  awkwardly mid-loop when the 2s cast time ends.
- **Old placeholder tracer removed**: `FireBolt.prefab`/`IceBolt.prefab`
  each had a `TrailRenderer` on the root (the invisible capsule that
  was drawing the trail line you referred to as "the simple tracer"),
  added before the actual VFX was tracking correctly. Now that the VFX
  itself tracks, removed the `TrailRenderer` component from both
  (the disabled placeholder MeshRenderer/SphereCollider on that same
  root were left alone - they're already inert, not visible, not
  colliding, and weren't part of what was asked). Please confirm you no
  longer see the plain trail line, only the VFX itself.
- **If it still doesn't look changed after these edits**: this has now
  happened across three separate substantive edits (Simulation Space,
  disabling the physics Rigidbody, and this tracer removal) - if none
  of them are visibly taking effect, the Unity Editor most likely hasn't
  reimported these files at all (they were all edited from outside the
  Editor). Before assuming another bug, try Assets → Reimport All (or
  right-click the specific prefab → Reimport) and confirm the Project
  window isn't showing a stuck import spinner, then retest.

## 36. Still outstanding from earlier sessions (unrelated to the above, just parked here)

- The VPS still runs the **pre-refactor server build** — its network
  protocol no longer matches this client at all. Nothing will connect
  until a fresh Linux Dedicated Server build is deployed.
- NavMesh mob pathfinding (mobs currently walk straight at you and can
  get stuck on walls) — not started, see `CLAUDE.md` "Not yet done".
