# encounter

## Core rules — never break these, no exceptions

1. **Never invent items, abilities, mobs, effects, or game mechanics.**
   Only execute what the user explicitly asks for. No "sample" content,
   no "while I'm here" mechanics, no filling in numbers or names the
   user didn't give (ask instead).
2. **Never edit code that is tangential to the thing currently being
   worked on.** If something out of scope looks like it needs changing
   (a bug, a refactor, a cleanup), write it down and ask the user; do not
   touch it unless they say so.

These come from the user directly (2026-09-11) after unrequested sample
items were added. They override any general helpfulness instinct.

A 3D multiplayer game (working title "encounter"; the working directory is
still named `reallyfungame`), built in Unity, hosted on a VPS the user
controls so they and friends can play together. Git: `github.com/n17230/encounter`
(user `n17230`).

## Tech baseline

- Unity **6.3 LTS** (`6000.3.23f1`), URP 17.3.0, Netcode for GameObjects
  2.13.2, Unity Transport 2.7.4, Multiplayer Play Mode 2.0.2 (the reason
  for the Unity 6 move — MPPM doesn't support 2022.3).
- Product name `encounter` (`ProjectSettings/ProjectSettings.asset`).
- **Git + LFS**: images/models/audio/fonts plus TerrainData `.asset`s go
  through LFS (`.gitattributes`). The four imported Asset Store packs
  (`Assets/PolysplitGames`, `Assets/Shinabro`, `Assets/Spells Pack`,
  `Assets/TriForge Assets`) are **gitignored and stay local** — 3.9 GB.
  The subset the game actually references (288 files, ~1 GB, found by a
  GUID-dependency crawl from our scene/prefabs/settings) was moved to
  `Assets/External/<Pack>/<original relative path>` with their `.meta`
  files, so every GUID reference survived. **If you start using another
  file from a pack, move it (plus `.meta`) into `Assets/External/`
  under the same relative path, or it won't be in the repo.** A fresh
  clone therefore builds without the packs installed. `Assets/_Recovery/`
  (Unity crash-recovery scenes) and `SERVER_INFO.md` (VPS access details)
  are also ignored.
- `.gitattributes` uses `[[:space:]]` globs for paths with spaces.

## Compile-checking without the Editor

The Unity Editor is usually open on this project, which locks it for a
second Unity instance, so scripts are compile-checked with a throwaway
SDK-style csproj that globs `Assets/Scripts/**` + `Assets/Tests/**` and
references the Editor's `UnityEngine/*.dll`, `Library/ScriptAssemblies/`
(`Unity.Netcode.Runtime`, `Unity.Collections`, `Unity.Mathematics`,
`Unity.Networking.Transport`, `UnityEngine.TestRunner`) and the
`nunit.framework.dll` from `Library/PackageCache/com.unity.ext.nunit@*`.
`dotnet build` (SDK 10 is installed) on that passes when Unity would
compile. It does not run tests or ILPP; EditMode tests run from the
Editor's Test Runner. New `.cs`/`.asmdef`/folders committed from outside
the Editor need hand-written `.meta` files (generate a random 32-hex GUID).

## Architecture (as of 2026-09-11)

Scripts live in `Assets/Scripts/` (assembly `Encounter`, see
`Encounter.asmdef`); tests in `Assets/Tests/EditMode/` (`Encounter.Tests`).
Third-party scripts under `Assets/External` remain in `Assembly-CSharp`.

- **Authority split**: *movement is owner-authoritative, combat is
  server-authoritative*. `Player.prefab`'s `NetworkTransform` is in
  `AuthorityMode: Owner`; `PlayerMovement` runs the `CharacterController`
  on the owning client only (input gathered per rendered frame, consumed
  per `FixedUpdate`; mouse yaw is summed and jump latched so nothing is
  dropped between physics steps). There is no movement RPC at all. The
  server reads replicated position/rotation for range/facing/LoS checks.
  Chosen over server-auth + prediction deliberately: friends-only game,
  instant feel matters, position cheating doesn't. Every damage/mana/
  cooldown/effect decision still happens only on the server.
  `PlayerRespawn`: server decides (`OnDeath` → `RestoreFull` +
  `PlayerMovement.ServerTeleportTo`, same path Recall uses — server-
  initiated, pre-arms the validator before the owner moves), the owner
  executes via `TeleportTo` → `NetworkTransform.Teleport` (only the
  authority may teleport). Mobs (`EnemyAI`) stay fully server-driven.
  - **Server-side movement validation** ("the MMO way" — decided
    2026-09-11 with going public in mind): `MovementValidator` (pure C#,
    tested) watches each *remote* owner's replicated transform in
    windows of 0.5 s — horizontal distance vs. `RunSpeed × elapsed × 1.3
    + 1 m` (using the max of the window's start/end speed so a fresh
    slow the client hasn't received yet can't trip it), ×3 that =
    teleport, feet more than 1 m under the terrain = below ground. A
    violation snaps the owner back (`CorrectPositionClientRpc` →
    `TeleportTo`), pauses checks for 1 s so in-flight states don't
    double-count, and 5 strikes within 30 s → `DisconnectClient`. The
    host's own player is exempt. Tunables are `[SerializeField]`s on
    `PlayerMovement`.
    - **Bug fixed 2026-09-11 — remote players falling through the map on
      spawn**: `ValidateReplicatedMovement` had no grace period, so a
      remote (non-host) player's very first tick could get judged
      against the prefab's raw baked spawn position — before
      `PlayerRespawn`'s owner-side terrain snap had round-tripped back
      to the server — which on a since-resculpted terrain can look
      below-ground, triggering a "correction" back to that same bad,
      genuinely-below-ground position. Fixed two ways: (1)
      `PlayerMovement.OnNetworkSpawn` now sets `validationResumeTime`
      `spawnGraceSeconds` (2s, longer than `correctionGraceSeconds`'s 1s
      on purpose — a fresh spawn has more to settle than a single
      correction) out, giving the owner-initiated snap time to land
      before the server judges anything; (2) `PlayerRespawn.HandleDeath`
      switched from its own `RespawnClientRpc` to
      `PlayerMovement.ServerTeleportTo` (the same path Recall uses) —
      death is server-detected, so it can pre-arm the validator
      *before* telling the owner to move, needing no grace period at
      all. Never seen actually fail in the Editor, only reasoned from
      the code — the host being exempt from validation is what explains
      the bug only ever showing up for the second (non-host) player.
  - **Client-side cast prediction**: `PlayerAbilities` pre-checks
    cooldown / mana (`CurrentMana` NetworkVariable) / target / range /
    facing locally and shows the reason instantly ("Out of range", …),
    starts the cooldown on key press (`predictedCooldownReady`, drives
    the new bottom-centre 8-slot ability bar with a cooldown sweep), and
    the server's start-of-cast rejections come back as
    `NotifyCastRejectedClientRpc(abilityId, reason)` which rolls the
    predicted cooldown/cast bar back. Cooldowns are otherwise never
    synced — the prediction is the client's only view of them.
  - **Mana is spent on successful cast, not on cast start** (changed
    2026-09-11): `CharacterStats.HasEnoughMana` (read-only) gates
    whether a cast is even allowed to start, in both
    `CastAbilityServerRpc` and `CastGroundTargetedAbilityServerRpc` —
    same rejection ("Not enough mana") as before, just no longer
    deducts anything. The actual `TrySpendMana` deduction moved into
    `ResolveAbility` (after every fizzle check — target lost/range/
    facing/LoS — has passed, right before the effect/projectile/recall
    actually happens) and the top of `ResolveGroundAbility`. A cast
    that fizzles during its cast-time window now costs nothing; only a
    cast that actually lands is charged. For instant-cast abilities
    this is a no-op in timing (resolve happens the same tick), but for
    Firebolt/Icebolt's 2 s cast it means the mana bar visibly drops
    when the cast *completes*, not when it starts.
  - **Ground-targeted abilities + forced movement** (WoW "Blizzard"-
    style, added 2026-09-11): `AbilityData.IsGroundTargeted` +
    `GroundEffectRadius` + `ForceSpeed` + `PushAway`. Pressing the
    hotkey doesn't cast immediately — it arms
    `PlayerAbilities.IsAimingGroundTarget`, which draws an owner-local
    `GroundTargetReticle` (a `TargetRingIndicator`-style procedural
    ring, never networked) following a mouse raycast that ignores the
    `Characters` layer so it hits terrain through players/mobs.
    Left-click (within `Range` of the caster) confirms and sends
    `CastGroundTargetedAbilityServerRpc(slot, worldPoint)`; the same
    hotkey again, or opening the menu, cancels aiming. `PlayerTargeting`
    ignores clicks entirely while aiming (`abilities.IsAimingGroundTarget`
    guard) so placement clicks can't also reselect your target. On
    resolve (after `CastTime`, same coroutine pattern as unit-targeted
    casts, no facing/LoS check — there's no single target to lose sight
    of), every alive `Targetable` (player or mob, caster included)
    within `GroundEffectRadius` of the point is driven toward a force
    target point at `ForceSpeed` via `PlayerMovement.ServerBeginPull` /
    `EnemyAI.ServerBeginPull`: for a pull (`PushAway` false) that point
    is the cast location itself; for a push (`PushAway` true) it's
    computed per-victim, radially outward past the edge of the radius —
    same underlying "drive toward a point" method either way, so the
    push/pull split lives entirely in `ResolveGroundAbility`'s target-
    point math, not in the movement code. For players this needed two
    things to actually work: a `ClientRpc` so the *owning* client
    (which alone may move its own `CharacterController`) performs the
    drag, and — the part that would otherwise silently break it —
    `MovementValidator` is fed a continuous `Reset` for the duration in
    `ValidateReplicatedMovement`, since the forced movement is faster
    than `RunSpeed` and would otherwise itself get read as a speed
    violation and snapped back. **Force Compression** (Id
    `force_compression`, was named Vacuum until renamed 2026-09-11) and
    **Force Expansion** (Id `force_expansion`, its opposite) are
    otherwise identical assets — 15-unit radius (the requested 30-unit
    diameter), instant cast, 25s cooldown, 240 mana, 15/s force speed,
    no damage/effect — differing only in `PushAway`. Range 30 and the
    force speed are still placeholder numbers, never specified beyond
    the diameter — flagged for tuning.
  - **Recall** (Id `recall`, added 2026-09-11): a plain unit-targeted
    spell (`RequiresTarget`, same click/Tab selection and range/facing/
    LoS checks as Firebolt/Icebolt) whose resolve does
    `AbilityData.RecallTarget` instead of damage — instantly teleports
    the *target* to the *caster's* position via the new
    `PlayerMovement.ServerTeleportTo` / `EnemyAI.ServerTeleportTo`
    (checked in that priority order ahead of `ProjectilePrefab` in
    `ResolveAbility`). For a player target this needed the same
    validator safety as a pull: `ServerTeleportTo` re-baselines
    `MovementValidator` and sets `validationResumeTime` *before* sending
    the `ClientRpc`, exactly like an existing movement-violation
    correction does — otherwise the server would read its own recall as
    a teleport-cheat and try to snap the player back. Mobs need no such
    care (server-driven already) — `EnemyAI.ServerTeleportTo` just
    disables/repositions/re-enables the `CharacterController` directly.
    Since players/mobs no longer collide with each other (see Character
    collision below), teleporting the target onto the caster is safe —
    no clipping/pushing. Cooldown 30s and mana cost 200 were set explicitly by the
    user (2026-09-11), and so was Range 40; instant cast and no threat
    generated are still unspecified placeholders.
- **Data assets + stable Ids** (`Scripts/Data/GameDatabase.cs`):
  `AbilityData`, `ItemData`, `StatusEffectData` each carry a `string Id`
  and are discovered with `Resources.LoadAll` from
  `Assets/Resources/Data/{Abilities,Items,Effects}/`. Ids (not list
  indices, not asset names) are what cross RPCs and get saved, so
  reordering/renaming/adding assets can't rebind anything. Loadout/gear
  sync RPCs send `;`-joined Id strings. **Adding content = drop a new
  asset in the right `Resources/Data` folder with a unique `Id`; no code
  or Inspector wiring.** Current Ids: abilities `firebolt`, `icebolt`;
  item `shield`; effects `burn`, `slow`. `WeaponData` (mob melee) lives in
  `Assets/Data/Weapons/` (referenced directly by mob prefabs, not looked
  up by Id). `Create Asset` menu paths are all under `Encounter/`.
- **Player profile** (`Scripts/Settings/PlayerProfile.cs`): one
  `[Serializable]` object for everything the local player has chosen —
  skill slot Ids + hotkeys (`KeyCode` + shift flag), gear Ids per
  `GearSlot`, movement `KeyCode[]` indexed by `MovementAction`, UI scale.
  `ProfileStore.Current` loads it from
  `Application.persistentDataPath/profile.json` on first access and
  `Save()` is called when leaving a menu panel / closing the Escape menu /
  entering the testing area. `Normalize()` repairs array lengths so old
  files stay loadable. This replaced the old `LoadoutSelection` /
  `GearSelection` / `MovementBindings` / `UIScale.Value` statics. Still
  local-only, not the account-backed unlock-gated profile from
  `DESIGN_IDEAS.md` — but it's the shape that will grow into it.
- **Combat pipeline** (`Scripts/Combat/`): `CharacterStats.ReceiveHit(in
  HitInfo)` is the *only* way anything hostile reaches a character —
  projectile impact, instant cast, mob melee, ground-patch refresh and
  DoT ticks all build a `HitInfo {Damage, ExtraThreat, AttackerClientId,
  Effect, EffectDuration}`. Damage is armor-mitigated (`1 - Armor/100`),
  1 threat per point of mitigated damage goes to the target's optional
  `ThreatTable`, `ExtraThreat` is added on top (taunts), then the effect
  is applied. `HitInfo.Source` (`Melee`/`Ability`/`GroundPatch`) says
  where a hit came from. **Effect immunities**: `ItemData.Immunities`
  (`EffectImmunity {Effect, GroundOnly}`) are registered on
  `CharacterStats` by source (the item) on equip, like stat modifiers,
  and checked in `ReceiveHit` before an effect is applied —
  `GearIceCleats` (Id `ice_cleats`, Boots) is immune to `slow` from
  ground patches only; a direct Icebolt still slows. **Auras**:
  `ItemData.Auras` (`ItemAura {Effect, Range}`) — `CharacterEquipment`
  pulses the effect (via `CharacterStats.ApplyEffect`, the non-hostile
  entry) onto every alive player within Range, self included, every 1 s
  with a 2.5 s duration, so it lapses on leaving range and same-asset
  auras don't stack. `GearAmuletOfReplenishment` (Necklace, Id
  `amulet_of_replenishment`, was Amulet of Mana / `GearManaAmulet` until
  renamed 2026-09-11) radiates `mana_aura` (+3 mana/s, i.e. +15 per the
  5 s regen tick — see Resource numbers below) at 40 range;
  `GearAmuletOfRegeneration` (Necklace, Id `amulet_of_regeneration`, was
  Amulet of Rejuvenation until renamed 2026-09-11) radiates `rejuvenation`
  (a periodic heal, not a regen-rate modifier — the underlying effect
  asset keeps its old Id/name, only the item was renamed) at 40 range.
  **Periodic healing**: `StatusEffectData.TickHeal` is the heal-side
  counterpart to `TickDamage` (either or both can be set;
  `StatusEffectTracker.Tick` schedules a tick if either is > 0) —
  `rejuvenation` is 10 hp every 5 s, applied via `CharacterStats.Heal`
  in `TickEffect`.
  **`StatType.ManaCostMultiplier`** (base 1,
  synced as `SyncedManaCostMultiplier`) is applied in
  `CharacterStats.TrySpendMana`; `GearStaff` (MainHand, 20 dmg / 2 s
  basic attack) gives −10% mana cost and +250 max mana (`StatType.MaxMana`,
  flat). **`StatType.DamageMultiplier`** scales all
  damage a player deals (applied in `DealDamage` via the attacker's
  stats, so DoT ticks count too). `GearFireTrinket` (Trinket, Id
  `fire_trinket`): +10% damage dealt, and a Range-0 aura of `burn` —
  i.e. the wearer is permanently Burning at the fire-patch rate (self-
  inflicted, no threat). Put new combat
  features (combat log, downed state, damage numbers) here, not at
  call sites.
- **Status effects**: `StatusEffectData` asset = `Id`, `DisplayName`,
  `Duration`, `TickDamage`/`TickInterval` (0 = no DoT), `List<StatBonus>`
  modifiers (same `StatBonus` struct gear uses; applied as `StatModifier`s
  with the asset as source). Runtime rules are in the pure-C#
  `StatusEffectTracker` (time passed in): same asset reapplied → **only
  ever extends** expiry (never shortens, never touches the tick schedule,
  so a due tick still fires); different assets stack independently; ticks
  catch up after a stall; callbacks run outside the dictionary walk so a
  tick that kills the target (→ `RestoreFull` → `ClearAll`) is safe.
  `CharacterStats` mirrors active effects into a
  `NetworkList<ActiveEffectNet>` (`Id` + expiry in `ServerTime`) purely
  for UI; `PlayerHUD` shows "Burning 2.3s" under own bars and the target
  frame. `DebuffType` enum is gone. Icebolt's direct hit uses
  `AbilityData.DirectHitEffectDuration` (5s) vs. the patch's
  `Effect.Duration` (3s).
- **Character collision**: players and mobs live on physics layer 8
  **Characters** (`TagManager.asset`), and Characters↔Characters is off in
  the Physics collision matrix (`DynamicsManager.asset`), so characters
  walk through each other while still colliding with everything on
  Default (terrain, props). `Player.prefab` and `MobNPC.prefab` roots
  carry the layer; mob variants inherit it. New character prefabs must
  be put on Characters too.
- **Resource numbers** (`Player.prefab`): 1000 health / 1000 mana. Regen
  is **discrete**: every `regenTickInterval` (5 s) the character gains
  rate × 5 — base mana regen 1/s → **5 mana per 5 s**, base health regen
  1.25/s → 6.25 per 5 s; the dead don't regen. Rates stay per-second so
  gear/aura bonuses read naturally (the Amulet's +1.5/s = +7.5 per tick).
  Firebolt/Icebolt cost **120 mana** (no cooldown, 2 s cast), so a full
  pool is ~8 casts and takes 1000/1 = ~17 min to refill from empty on
  base regen alone — mana is meant to be a real constraint now.
- **Synced derived stats**: `Stat` modifiers (gear, effects) only exist
  server-side, so `CharacterStats` mirrors `MaxHealth`/`MaxMana`/`RunSpeed`
  into `SyncedMaxHealth`/`SyncedMaxMana`/`SyncedRunSpeed`
  `NetworkVariable`s each server `FixedUpdate` (compare-then-write).
  **UI and owner movement must read the `Synced*` values**, never the
  `Stat.Value` on a client — that's the latent bug this fixed (a
  +MaxHealth item would have shown the wrong bar everywhere).
- **Gear**: `CharacterEquipment` re-syncs on `MainMenu.Closed`; only the
  first server-side application calls `RestoreFull()`, later swaps
  `ClampToMax()` (no free mid-fight heal). Wrong-slot items are rejected
  server-side. `CharacterStats.GetStat(StatType)` is the shared
  stat lookup.
- **Enemy targeting**: `TargetingMode` (`Proximity`/`HighestThreat`/
  `LowestThreat`/`FarthestPlayer`) and the pure `TargetSelector.Select`
  live in `Scripts/Enemy/TargetSelector.cs`; `EnemyAI` just builds
  `TargetCandidate`s (threat, distance) from connected alive players.
  Whether a mob participates in threat is still purely "does it have a
  `ThreatTable` component" (ogres yes, goblins no).
- **Menus / dev UI** (`Scripts/UI/`, all IMGUI `OnGUI`, deliberately
  disposable — don't invest in it; real UI should be UI Toolkit): pregame
  `MainMenu` (Choose Skills / Choose Gear / Options / Enter Testing Area)
  and, after `TestingAreaGate.Entered`, the same panels as an **Escape
  menu** (`MainMenu.IsOpen`). The Gear page is a paper-doll: 3×5 grid of
  equipment slots on the left, inventory grid (= every unequipped item in
  the game, no real inventory yet) on the right; items are an "X"
  placeholder with a hover tooltip until there's 2D art. While open, `PlayerMovement`, `PlayerCamera`,
  `PlayerTargeting`, `PlayerAbilities` ignore gameplay input; on close
  `MainMenu.Closed` triggers loadout/gear re-sync. Options page: UI scale
  (−/+ 25% steps, 75–250%) and movement rebinding (any non-mouse key;
  binding a key steals it from other movement actions *and* ability
  slots, and vice versa). Ability hotkeys are limited to 1–5, Shift+1–5,
  F1–F5, Q/E/R/T/F/G. `DevGui.Begin()` (UI scale) must be the first line of every
  `OnGUI`, laying out against `UIScale.Width/Height`. The Escape menu
  also has a **Summon Mobs** page (`PlayerSummon` on the local player
  object does the spawning; the menu just drives it). **No text fields
  in in-game panels**: IMGUI's native Tab focus traversal moves keyboard
  focus into any focusable control even when the Tab event is `Use()`d,
  and Tab is the tab-targeting key — the summon count is −/+ buttons
  for exactly that reason. The only text field is the server-address
  box, which is gone once connected.
- **Party frames** (`Scripts/UI/PartyFrames.cs`, drawn from `PlayerHUD`,
  top-right, added 2026-09-11): a health+mana row for every *other*
  connected player, WoW-party-style. No party system exists — this
  simply lists every player with a `PlayerMovement` (all of them, since
  it's a shared open lobby). Ordering is identical on every client with
  no synced state at all: sort by `OwnerClientId` (server-assigned,
  already known identically everywhere via each `CharacterStats`'
  `NetworkBehaviour.OwnerClientId`), label by that sorted position
  ("Player 1", "Player 2", …) — that's `Slot.PartyNumber`, a *stable
  identity* every viewer agrees on. Each viewer's own entry is skipped —
  not left as a blank row — so the remaining rows stack up from the top
  with no gap. `CurrentHealth`/`CurrentMana`/`SyncedMaxHealth`/
  `SyncedMaxMana` were already `NetworkVariable`s readable by everyone,
  so this is pure client-side rendering — no new syncing needed.
  - **F1–F5 party targeting** (fixed, not rebindable — added
    2026-09-11): `PlayerTargeting` targets whoever is drawn in that
    *row* on the viewer's own screen (F2 = second row), via
    `PartyFrames.GetDisplayOrder`'s returned order — **not** the same
    as `PartyNumber`. Row index is viewer-relative (depends on which
    entry got skipped for being "you"); `PartyNumber` is the same for
    everyone regardless of who's watching. Example: canonical order
    P1,P2,P3,P4 — P2's screen shows rows [P1, P3, P4] labeled "Player
    1"/"Player 3"/"Player 4"; P2's F2 hits row 1 → P3, even though P3's
    own label says "Player 3", not "Player 2". **Known conflict, not
    resolved**: F1–F5 are also selectable ability hotkeys in
    `MainMenu.AllowedKeyBindings` — a player who binds an ability there
    will trigger both the ability and party-targeting on the same
    press. Flagged, not fixed.
- **Minimap** (`Scripts/UI/Minimap.cs`, drawn from `PlayerHUD`): circular
  radar bottom-right, north-up, player at centre with a heading tick.
  **Compass letters** (added 2026-09-11): N/E/S/W drawn at fixed screen
  positions just inside the rim, tied to the same fixed world axes the
  map's north-up orientation already used (+Z = N, +X = E) — since the
  map never rotates to face the player, these need no per-player state
  and are identical for everyone by construction.
  **Blank by default**: blips only draw for what the local player's
  equipped gear reveals (`ItemData.Reveals`, `MinimapReveal` flags
  Players/Mobs, unioned across worn items, read client-side from the
  profile). Revealed `Targetable`s within 50 world units draw as blips
  (players green, mobs **always** red — mob pings never take the
  target-highlight color even if the pinged mob is your current target;
  that distinction was removed 2026-09-11 per explicit request, live
  player blips still turn yellow on your target) - **Players reveal is
  live**, but **Mobs reveal is a pulse, not a tracker**: `PlayerHUD`
  snapshots every mob's position every `MobPingInterval` (5s) into
  `mobPingPositions` (`List<Vector3>`, frozen — not the mob's live
  transform; this used to carry the mob's `Targetable` too for the
  highlight, simplified to plain positions once that was dropped),
  fading the dots out over `MobPingFadeDuration` (4s) before the next
  pulse, so there's a ~1s blind gap each cycle; `Minimap.Draw` takes
  `mobPingPositions`/`mobPingAlpha` alongside the live `blips`/`reveals`.
  `GearEcholocator` (Trinket, Id `echolocator`)
  grants the Mobs reveal. The reverse direction is
  `ItemData.BroadcastsLocation` → server-written
  `CharacterEquipment.BroadcastsLocation` NetworkVariable on the wearer;
  allies' minimaps draw a broadcasting player regardless of their own
  reveals (`GearTransmittingBeacon`, Ring1, Id `transmitting_beacon`).
  **Gear slots: 13, not 15** — `GearSlot` has `Ring1`/`Ring2` only
  (`Ring3`/`Ring4` removed 2026-09-11), and those two are
  **interchangeable**: a ring item's `Slot` is just the `Ring1` category,
  `GearSlotExtensions.IsRing()` treats either physical slot as valid for
  it in `CharacterEquipment.SetGearServerRpc`'s placement check, and
  `MainMenu.TargetSlotFor` picks whichever physical ring slot is free
  (Ring1 first) when equipping one from the inventory grid.
  No terrain by design.
  Disc/blip textures are generated at runtime.
- **Controls**: W/S forward/back (both mouse buttons also = forward), A/D
  strafe, Space jump, `\` auto-run (cancelled by W/S or opening the
  menu), right-drag turns the body, left-drag free-looks the camera,
  left-click / Tab targets, Escape clears the target first and opens the
  menu only when nothing is targeted.
  **Right-clicking a mob** (a click, not a drag) targets it and arms
  **auto-attack** (`PlayerAutoAttack`; **T** toggles it on/off for the
  current target too — `MovementAction.AutoAttack`, rebindable on the
  Options page): the server swings the equipped
  MainHand item's `WeaponData` (or the `Fists` fallback wired on the
  prefab) every `SwingInterval` while the target is within `Range` and
  inside the facing cone, stays armed while closing distance, follows
  Tab target changes, and disarms on untarget/death. **All basic attacks reach
  `WeaponData.BasicAttackRange` = 2** (one constant, mobs and players
  alike; melee *abilities* use their own `AbilityData.Range`);
  `WeaponData` owns `SwingInterval`, and `EnemyAI` reads it from its
  main-hand weapon (its own `attackInterval` is only the unarmed
  fallback). `ItemData.Weapon` links a MainHand item to its weapon
  (`GearBroadSword` → `WeaponBroadSword`, 40 dmg / 2 s, +100 max health
  flat, and +20% threat generated via `StatType.ThreatMultiplier`,
  applied to the attacker's threat in `CharacterStats.AddThreat`; Fists
  are 15 dmg / 1.5 s).
- **Testing lobby scope** (still placeholders, not the real designs):
  instant respawn at map centre, `PlayerSummon` (Escape menu → Summon Mobs, spawning
  `MobGoblin`/`MobOgre` variants on a circle of `mapHalfExtent`), no
  wipe/reset encounter model, no loot, no unlocks.

## Asset layout

`Assets/Prefabs/{Player,Mobs,Mobs/Visuals,Projectiles,Patches}`,
`Assets/Materials`, `Assets/Animation`, `Assets/Resources/Data/*`,
`Assets/Data/Weapons`, `Assets/Scenes/SampleScene.unity`, `Assets/Settings`
(URP), `Assets/External` (used third-party subset). Root-level
`DefaultNetworkPrefabs.asset`, `New Terrain.asset` etc. are Unity-managed.

## Networking / hosting

- Dedicated Linux server on a VPS (`SERVER_INFO.md`, gitignored, has the
  details and a hard-won-lessons section: `ServerListenAddress` loopback
  default, `UNITY_SERVER` being defined in the Editor, Git-Bash `scp -r`
  silently dropping files). Server still runs via manual `nohup`, not
  systemd. The scene's `UnityTransport` is authored with the VPS IP (what
  a shipped build dials by default); `NetworkBootstrap`'s panel has an
  address field that defaults to `127.0.0.1` in the Editor (incl. MPPM
  virtual players) and remembers the last address in the profile.
- A Windows Standalone build was sent to the user's brother and works
  end-to-end against the VPS.

## Not yet done

1. systemd unit for the dedicated server, and **redeploy a fresh server
   build** — the VPS still runs the pre-refactor build, whose network
   protocol no longer matches (new NetworkVariables/RPC signatures).
2. Fire/ice ground patches as URP Decal Projectors (visual only; needs the
   Decal renderer feature added to the three URP renderer assets in the
   Editor first) and real particle VFX instead of coloured discs.
3. The designed systems in `DESIGN_IDEAS.md` / `ARCHITECTURE_NOTES.md`:
   loot, account-backed unlock-gated profile, encounter wipe/reset state
   machine, taunt/threat reset on combat end, two-handed weapons.
4. Consider downsizing the largest TriForge textures in `Assets/External`
   (several 50–100 MB 4K PNGs) — LFS is ~1.1 GB, near GitHub's free tier.
5. **Mob pathfinding via NavMesh** — `EnemyAI` currently steers straight
   at its target and runs into walls. Plan: add `com.unity.ai.navigation`
   to the manifest, rewrite mob steering to follow `NavMesh.CalculatePath`
   corners with the existing `CharacterController` (straight-line chase
   as fallback when no path exists); the user adds a `NavMeshSurface` to
   the terrain and bakes in the Editor (rebake after terrain/prop
   changes). Decided 2026-09-11, not started.

## Notes for future sessions

Treat the above as established direction. `DESIGN_IDEAS.md` is game
design, `ARCHITECTURE_NOTES.md` is how the code must be shaped to serve
it, `BOSS_DESIGN.md` is encounter design. The refactor on 2026-09-11 was
done on branch `refactor/architecture` and merged to `main`.
