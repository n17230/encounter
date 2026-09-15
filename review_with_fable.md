Claude, this is a list of things you need to review. Please follow these instruction.

 - Batch reads. Before reading any code, list every file the punch-list items reference and read each one exactly once, even if multiple items touch it. Don't re-open a file already read.
 - Group by subsystem, not by list order. Cluster items that hit the same script/asset (e.g. all ground-targeted-casting items, all status-effect items) and diagnose each cluster in one pass instead of round-tripping per item.
 - No narration. Don't describe what you're about to do or summarize your process. Go straight to reading, then straight to output.
 - Do not report on things that are okay as is. Only point out issues or non-ideal things you find.
 - Terse output only. For each open item, one line: item name, verdict (likely bug / needs live test / blocked on Editor step), and a single sentence or 2 describing the problem. and then 1 sentence or 2 describing the fix.
 - If something requires live/server testing to resolve either way, say that in three words or less ("needs live test") rather than speculating at length.
 - you may speak between caveman and normal to help reduce token usage. Do not go 100% caveman.
 - Each of the items below are new mechanics or things I need you to review (Note: the enumeration has gaps due to deleting items). If you think a refactor is in place, feel free to move things around. Note - at the time you are reading this, everything works correctly.

## 0. Buff/aura/spell VFX indicators - in particular, VFX homing on spells like firebolt and icebolt
  - current bug, the aura VFX stay at the location at which the animation started, instead of the animation following the player.


## 1. Per-mob Animator Controllers, generic run clip - never run, touches every mob's animation

## 2. UI Scale / Look Sensitivity are now sliders - never run

## 3. Camera zoom (Up/Down arrow) - never run

## 4. AoE now hits anyone in range, players and mobs alike, except the caster

- **Fire/ice ground patches are a separate mechanic and unaffected by
  this** — `GroundPatch.cs`'s trigger already hits anyone who steps in
  it, caster included. 

## 5. Slows no longer stack across different effects (new global rule). Only the single strongest slow can ever be active on a character at once, permanently, for every slow in the game

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

## 8. Server-wide item uniqueness (new mechanic)

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

## 10. Soul Siphon (new mechanic: tick lifesteal)
## 11. Armor floored at 0
## 12. Armorbreaker (new mechanic: stacking effects)

- **Armorbreaker** (new 1H MainHand weapon, 40 dmg / 2s swing): on hit,
  applies a stacking armor-reduction debuff - each hit adds a stack
  (−2 Armor per stack) up to 5 stacks (−10 Armor total at max), 10
  seconds duration, shared across all current stacks (i.e. one timer for
  the whole debuff, not a separate 10s per stack - getting hit again
  resets the timer for everything currently stacked, it doesn't extend
  or stack the *duration*, only the *magnitude*, up to the cap).

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

## 16. Healing now generates threat - 15% of the healed amount as threat

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


## 19. Boots of Lightness (new air-hover mechanic)

- **Boots of Lightness (genuinely new movement mechanic, never run)**:
  equip them, jump, then press Jump again while still in the air.
  player stops falling for ~2 seconds, and  WASD freely steers you around the whole time - not stationary.


## 23. Seven new warrior spells/item (Reaper's Wheel, Trample, Cleave, Team
   Up, Crippling Blow, Seismic Slam, Barbarian's Mantle) — biggest and
   most novel batch yet, several brand-new mechanics

## 24. Five new healing spells + shields + Holy Scepter — biggest untested batch yet

## 27. Force Compression / Force Expansion / ground-targeted casting (highest risk — newest, most moving parts)

## 28. Recall

## 29. One For All (damage redirect) —  new mechanic

## 30. Auto-attack. Right-click a mob to start attacking

## 31. Party frames + F1–F5 targeting + minimap compass

## 32. Minimap / Echolocator

## 33. Gear — ring slots and the recent renumbering

## 35. Firebolt/Icebolt VFX homing

## 36. Entirely new required mechanics:

- NavMesh mob pathfinding (mobs currently walk straight at you and can
  get stuck on walls) — not started, see `CLAUDE.md` "Not yet done".

