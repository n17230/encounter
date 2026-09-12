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

These are absolute — they override any general helpfulness instinct.

A 3D multiplayer game (working title "encounter"; the working directory is
still named `reallyfungame`), built in Unity, hosted on a VPS the user
controls so they and friends can play together. Git: `github.com/n17230/encounter`
(user `n17230`).

## Tech baseline

- Unity **6.3 LTS** (`6000.3.23f1`), URP 17.3.0, Netcode for GameObjects
  2.13.2, Unity Transport 2.7.4, Multiplayer Play Mode 2.0.2 (needed for
  testing multiple clients in-editor — MPPM requires 2023.1+).
- Product name `encounter` (`ProjectSettings/ProjectSettings.asset`).
- **Git + LFS**: images/models/audio/fonts plus TerrainData `.asset`s go
  through LFS (`.gitattributes`). The four imported Asset Store packs
  (`Assets/PolysplitGames`, `Assets/Shinabro`, `Assets/Spells Pack`,
  `Assets/TriForge Assets`) are **gitignored and stay local** — 3.9 GB.
  The subset the game actually references (288 files, ~1 GB) lives in
  `Assets/External/<Pack>/<original relative path>` with their `.meta`
  files, so every GUID reference resolves. **If you start using another
  file from a pack, move it (plus `.meta`) into `Assets/External/` under
  the same relative path, or it won't be in the repo.** A fresh clone
  builds without the packs installed. `Assets/_Recovery/` and
  `SERVER_INFO.md` (VPS access details) are also gitignored.
- `.gitattributes` uses `[[:space:]]` globs for paths with spaces.

## Compile-checking without the Editor

The Unity Editor is usually open on this project, which locks it for a
second Unity instance, so scripts are compile-checked with a throwaway
SDK-style csproj (`Tools/CompileCheck.csproj`) that globs
`Assets/Scripts/**` + `Assets/Tests/**` and references the Editor's
`UnityEngine/*.dll`, `Library/ScriptAssemblies/` (`Unity.Netcode.Runtime`,
`Unity.Collections`, `Unity.Mathematics`, `Unity.Networking.Transport`,
`UnityEngine.TestRunner`) and the `nunit.framework.dll` from
`Library/PackageCache/com.unity.ext.nunit@*`. `dotnet build` on that
passes when Unity would compile. It does not run tests or ILPP; EditMode
tests run from the Editor's Test Runner. New `.cs`/`.asmdef`/folders
committed from outside the Editor need hand-written `.meta` files
(generate a random 32-hex GUID). A **prefab** with a `NetworkObject`
cannot be safely hand-authored this way — its `GlobalObjectIdHash` and
mesh/material references need the Editor to actually serialize them;
that kind of asset is left as an Editor step for the user (see "Not yet
done").

## Architecture

Scripts live in `Assets/Scripts/` (assembly `Encounter`, see
`Encounter.asmdef`); tests in `Assets/Tests/EditMode/` (`Encounter.Tests`).
Third-party scripts under `Assets/External` remain in `Assembly-CSharp`.

- **Authority split**: movement is owner-authoritative, combat is
  server-authoritative. `Player.prefab`'s `NetworkTransform` is
  `AuthorityMode: Owner`; `PlayerMovement` runs the `CharacterController`
  on the owning client only, no movement RPC exists. The server reads
  replicated position/rotation for range/facing/LoS checks; every
  damage/mana/cooldown/effect decision happens only on the server.
  Chosen for a friends-only game where instant feel matters more than
  position-cheat resistance. `PlayerRespawn` calls `RestoreFull()` then
  `PlayerMovement.ServerTeleportTo` (server-initiated, pre-arms the
  movement validator before the owner moves); the owner executes via
  `TeleportTo` → `NetworkTransform.Teleport` (only the authority may
  teleport). Mobs (`EnemyAI`) are fully server-driven.
  - **Movement validation**: `MovementValidator` (pure C#, tested)
    watches each *remote* owner's replicated transform in 0.5s windows —
    horizontal distance vs. `RunSpeed × elapsed × 1.3 + 1m` (teleport
    threshold is 3× that), feet more than 1m under terrain = below
    ground. A violation snaps the owner back, pauses checks for 1s, and
    5 strikes within 30s disconnects the client. The host's own player
    is exempt. `PlayerMovement.OnNetworkSpawn` opens a `spawnGraceSeconds`
    (2s) window before validation starts, since a remote player's
    owner-side terrain snap needs a round trip to reach the server
    first.
  - **Client-side cast prediction**: `PlayerAbilities` pre-checks
    cooldown/mana/target/range/facing locally, starts the predicted
    cooldown on key press (drives the ability bar's cooldown sweep), and
    rolls it back if the server rejects the cast
    (`NotifyCastRejectedClientRpc`). Cooldowns are otherwise never
    synced.
  - **Mana is spent on successful cast, not cast start**:
    `HasEnoughMana` (read-only) gates whether a cast can start;
    `TrySpendMana` only fires in `ResolveAbility`/`ResolveGroundAbility`,
    after every fizzle check has passed. A fizzled cast costs nothing.
  - **Ground-targeted casting** (WoW "Blizzard"-style):
    `AbilityData.IsGroundTargeted` + `GroundEffectRadius` + `ForceSpeed`
    + `PushAway`. The hotkey arms `PlayerAbilities.IsAimingGroundTarget`,
    drawing an owner-local `GroundTargetReticle` following a mouse
    raycast that ignores the Characters layer; left-click within `Range`
    confirms and sends `CastGroundTargetedAbilityServerRpc`. On resolve,
    every alive `Targetable` within `GroundEffectRadius` is driven
    toward a force target via `PlayerMovement.ServerBeginPull`/
    `EnemyAI.ServerBeginPull` — a pull targets the cast point directly,
    a push computes a point radially outward per-victim. For players
    this needs `MovementValidator` fed a continuous `Reset` for the
    duration, since forced movement exceeds `RunSpeed` and would
    otherwise read as a violation. Force Compression and Force
    Expansion are the same shape with `PushAway` flipped (a pull
    targets the cast point; a push sends victims radially outward).
  - **Recall**: unit-targeted, teleports the target to the caster's
    position via `ServerTeleportTo`/`EnemyAI.ServerTeleportTo` (checked
    ahead of `ProjectilePrefab` in `ResolveAbility`). Re-baselines
    `MovementValidator` before the teleport lands so the server doesn't
    read its own recall as cheating.
  - **Damage redirect**: `StatusEffectData.DamageRedirectPercent` — if
    the victim has an active effect with this set, `DealDamage` peels
    that fraction off the already-mitigated damage and sends it to
    whoever applied the effect, via `CharacterStats.ApplyRawDamage`
    (shared health-loss/death-check helper). Only the strongest active
    redirect applies; threat is still calculated from the full mitigated
    amount against the original victim. `AbilityData.ExclusiveSingleTarget`
    limits an ability to one currently-affected target **per caster**
    (`PlayerAbilities.exclusiveTargets`), stripping the effect from the
    previous holder when recast on someone new. One For All's effect
    uses `EffectStackingMode.Override` so a different caster's cast
    takes the bond over outright instead of the two applications racing
    on remaining duration.
  - **Damage reflection**: `StatType.DamageReflectPercent` (gear-driven,
    unlike the effect-driven redirect above) — in `DealDamage`, a
    fraction of the mitigated hit is dealt straight back to the attacker
    via `ApplyRawDamage` (same no-remitigation, no-chain reasoning as
    redirect). Resolving *who* the attacker is normally goes through
    `AttackerClientId` (a real clientId, so it only ever resolves
    players) — `HitInfo.Attacker` is an optional direct `CharacterStats`
    reference added specifically so this also works against mobs, which
    have no clientId at all; `EnemyAI`'s melee hit is the only source
    that currently sets it. Aegis of Reflection grants this stat.
  - **Healing and shields**: `HitInfo` carries `Heal` and `ShieldAmount`
    alongside `Damage`, all handled in `ReceiveHit`.
    `CharacterStats.Heal(amount, healerClientId)` scales by the healer's
    `HealingMultiplier` stat and generates threat (see Enemy targeting
    below); aura-pulsed healing passes `NoAttacker` so it gets neither
    the multiplier nor the threat. `ShieldAmount`
    (`NetworkVariable<float>`) absorbs damage before health in
    `DealDamage`; a new grant replaces any remainder rather than
    stacking, and it has no duration cap — it persists until consumed.
    `AbilityData.AreaAroundCaster` resolves centered on the caster's own
    position, hitting every player (not mobs) within `GroundEffectRadius`.
  - **Persistent structures**: `AbilityData.IsPersistentStructure` +
    `StructurePrefab`/`StructureWidth`/`StructureHeight`/
    `StructureThickness`, ground-targeted. On resolve,
    `PlayerAbilities.ResolvePersistentStructure` spawns `StructurePrefab`
    at the aimed point (rotated so its width axis is perpendicular to
    the caster's current facing), scaled by `PlacedStructure`
    (`Scripts/Abilities/PlacedStructure.cs`, `RequireComponent(BoxCollider)`,
    non-trigger — a real obstacle, unlike `GroundPatch`'s trigger). It
    has no lifetime; `PlayerAbilities.activeStructures` (per-caster,
    per-ability) despawns the previous one when the same caster casts
    the same ability again. Earthen Bastion uses this — its prefab
    still needs Editor setup, see "Not yet done".
  - **Following zones**: `AbilityData.IsFollowingZone` +
    `FollowingZonePrefab`, unit-targeted (not ground-targeted). On
    resolve, `PlayerAbilities.ResolveFollowingZone` spawns
    `FollowingZonePrefab` on the target and calls `Initialize` with the
    target's `Transform`, `Effect`, and two fields reused from the
    ground-patch system for their existing meaning — `GroundEffectRadius`
    (zone radius) and `PatchDuration` (how long the zone lasts). The new
    `FollowingZone` component (`Scripts/Abilities/FollowingZone.cs`,
    `RequireComponent(SphereCollider)`) is `GroundPatch`'s trigger/
    occupant-refresh logic with two additions: every `FixedUpdate` it
    re-centers itself on the followed `Transform` (so the zone chases
    its target instead of staying put), and it never affects the caster
    themselves even if they end up standing inside it (matched by
    `NetworkObjectId`, not `clientId`). Despawns itself once its own
    duration elapses, independent of what happens to the target. Arctic
    Winds uses this — its prefab still needs Editor setup, see "Not yet
    done".
  - **Weapon-scaling damage**: `AbilityData.WeaponDamagePercent` (0 =
    none) — total damage is `Damage + WeaponDamage × WeaponDamagePercent`,
    computed in `PlayerAbilities.ResolveTotalDamage` and used everywhere
    an ability's damage is read. `StatType.WeaponDamageBonus` adds a
    flat bonus to weapon damage itself, read by both
    `PlayerAutoAttack`'s basic swing and `ResolveWeaponDamage`, so it
    also flows proportionally into `WeaponDamagePercent` abilities.
  - **Melee-only gating**: `AbilityData.RequiresMeleeWeapon` blocks
    casting unless `CharacterEquipment.MainHandWeapon` is non-null
    (fists don't count) — checked client-side (via the profile) and
    server-side. `AbilityData.RequiresShield` is the same pattern for
    `ItemData.IsShield` in OffHand (`CharacterEquipment.HasShieldEquipped`).
  - **Self-buffs**: `AbilityData.SelfBuff` — no targeting at all, `Effect`
    is applied directly to the caster's own `CharacterStats`. Distinct
    from `AreaAroundCaster` (hits every ally in radius) and aura spells
    (permanent, gear-less) — this is just a plain timed buff on whoever
    cast it. **Block chance**: `StatType.BlockChancePercent` — rolled in
    `CharacterStats.RollBlock` against any `HitSource.Melee` hit,
    zeroing the damage outright on success. Gear and effects both just
    add `Flat` `StatModifier`s to it like any other stat, so they stack
    additively through the normal `Stat` machinery (e.g. a 5%-from-gear
    shield plus a 25%-from-effect buff nets 30%) rather than one
    overriding the other. Aegis of the Ancient (`RequiresShield`,
    `SelfBuff`) grants +25% for its duration.
  - **Caster-facing AoEs**: `AbilityData.EnemiesAroundCaster`/
    `ConeAroundCaster` hit every non-player `Targetable` within
    `GroundEffectRadius` (the cone variant also filtered by `ConeAngle`
    via `FacingCone.IsWithin`) — used by Reaper's Wheel/Seismic Slam
    (circle) and Cleave (cone). `ChargeForwardDistance`/`ChargeToTarget`
    drive the caster via `PlayerMovement.ServerBeginPull`: the former
    (Trample) charges straight forward a fixed distance, hitting every
    enemy within 2.5 units of the line; the latter (Team Up) charges to
    just short of a unit target and applies `Effect` to the *target*,
    not the caster — a support "peel" ability, whose effect is a full
    damage redirect to the caster (reuses the One For All redirect
    mechanism, just short-duration and 100% instead of long-duration and
    partial).
  - **Stun**: `StatusEffectData.IsStun`, read via
    `CharacterStats.IsStunned`. Only `EnemyAI` obeys it (freezes
    movement/attacks) — no ability currently stuns a player.
  - **HP-threshold item bonuses**: `ItemData.HpThresholdEffects` —
    `CharacterEquipment` evaluates it once/sec and swaps which side's
    `StatBonus`es are applied when the wearer's health fraction crosses
    `HealthPercentThreshold`, keyed by `(item, index, above/below)`
    tuples so both sides' modifiers can be independently removed.
    Barbarian's Mantle uses this (armor above the threshold, damage
    below it).
  - **Two-handed weapons**: `ItemData.TwoHanded` — a two-handed MainHand
    item occupies OffHand too. Enforced in `MainMenu`'s gear-equip click
    handler (auto-clears the conflicting slot) and authoritatively in
    `CharacterEquipment.SetGearServerRpc` (MainHand, slot 11, is always
    processed before OffHand, slot 12, so OffHand is forced null if
    MainHand resolved to a two-handed item).
  - **Dispel**: `AbilityData.RemovesNegativeEffect` strips one active
    `StatusEffectData.IsNegative` effect from the target (arbitrary pick
    if more than one is active) via `CharacterStats.RemoveOneNegativeEffect`.
    Every buff/aura effect defaults `IsNegative` to false and is never
    dispellable.
  - **Aura spells**: `AbilityData.IsAuraSpell` + `AuraRange` +
    `AuraReveals` — cast once (no target), grants a permanent, gear-less
    aura that never expires. `CharacterEquipment` tracks at most one
    active cast-aura per caster (`SetActiveAura`), so casting a
    different aura spell always replaces the previous one; pulsed every
    `FixedUpdate` tick the same way an item's own `Auras` are
    (`PulseAura`). A reveal-only aura is carried via
    `NetworkVariable<bool> CastAuraRevealsMobs`, read by `PlayerHUD`
    alongside `ItemData.Reveals`. Echolocation is the reveal-only case
    (`AuraReveals`, no `Effect`); Aura of Replenishment/Regeneration
    each pulse an `Effect` instead.
  - **Global cooldown**: starting any cast locks out starting a
    different one for 1.5s, on top of that ability's own cooldown — one
    shared gate across every slot. Same predict-on-client/
    confirm-or-rollback-via-server pattern as per-ability cooldowns
    (`predictedGlobalCooldownReady` / `serverGlobalCooldownReadyTime` in
    `PlayerAbilities`). No dedicated UI; a blocked cast shows the normal
    transient rejection notice.
  - **Air hover**: `ItemData.GrantsAirHover` — while airborne, pressing
    Jump again (once per airtime, re-armed on landing) suspends gravity
    for `hoverDuration` (2s) while WASD keeps steering normally, unlike
    a normal fall (which locks in launch-time momentum). Fully
    owner-local, consistent with the rest of `PlayerMovement`'s trust
    model — no server round trip, and no interaction with
    `MovementValidator` (hovering only affects vertical velocity; the
    validator only polices horizontal speed and below-ground). **Boots
    of Lightness** grants it.
- **Data assets + stable Ids** (`Scripts/Data/GameDatabase.cs`):
  `AbilityData`, `ItemData`, `StatusEffectData` each carry a `string Id`
  and are discovered with `Resources.LoadAll` from
  `Assets/Resources/Data/{Abilities,Items,Effects}/`. Ids (not list
  indices, not asset names) are what cross RPCs and get saved, so
  reordering/renaming/adding assets can't rebind anything. Loadout/gear
  sync RPCs send `;`-joined Id strings. **Adding content = drop a new
  asset in the right `Resources/Data` folder with a unique `Id`; no code
  or Inspector wiring.** `WeaponData` (mob/player melee+ranged basic
  attacks) lives in `Assets/Data/Weapons/` (referenced directly by mob
  prefabs and `ItemData.Weapon`, not looked up by Id). `Create Asset`
  menu paths are all under `Encounter/`.
- **Player profile** (`Scripts/Settings/PlayerProfile.cs`): one
  `[Serializable]` object for everything the local player has chosen —
  skill slot Ids + hotkeys (`KeyCode` + shift flag), gear Ids per
  `GearSlot`, movement `KeyCode[]` indexed by `MovementAction`, UI scale.
  `ProfileStore.Current` loads it from
  `Application.persistentDataPath/profile.json` on first access and
  `Save()` is called when leaving a menu panel / closing the Escape menu
  / entering the testing area. `Normalize()` repairs array lengths so
  old files stay loadable. Local-only, not the account-backed
  unlock-gated profile `DESIGN_IDEAS.md` describes — but it's the shape
  that will grow into it.
- **Combat pipeline** (`Scripts/Combat/`): `CharacterStats.ReceiveHit(in
  HitInfo)` is the *only* way anything hostile reaches a character —
  projectile impact, instant cast, mob melee, ground-patch refresh and
  DoT ticks all build a `HitInfo {Damage, ExtraThreat, AttackerClientId,
  Effect, EffectDuration}`. Damage is armor-mitigated (`1 - Armor/100`,
  `Armor` floored at 0 — `Stat`'s optional `minValue` constructor
  parameter, defaulting to no floor for every other stat — so stacked
  Armor-reducing debuffs like Armorbreaker's can't push it negative and
  invert the mitigation formula into bonus damage taken),
  1 threat per point of mitigated damage goes to the target's optional
  `ThreatTable`, `ExtraThreat` is added on top (taunts), then the effect
  is applied. `HitInfo.Source` (`Melee`/`Ability`/`GroundPatch`) says
  where a hit came from. **Effect immunities**: `ItemData.Immunities`
  (`EffectImmunity {Effect, GroundOnly}`) are registered on
  `CharacterStats` by source (the item) on equip, like stat modifiers,
  and checked in `ReceiveHit` before an effect is applied —
  `GearIceCleats` (Boots) is immune to `slow` from ground patches only;
  a direct Icebolt still slows. **Auras**: `ItemData.Auras`
  (`ItemAura {Effect, Range}`) — `CharacterEquipment` pulses the effect
  (via `CharacterStats.ApplyEffect`, the non-hostile entry) onto every
  alive player within Range, self included, every 1s with a 2.5s
  duration, so it lapses on leaving range and same-asset auras don't
  stack. **Periodic healing**: `StatusEffectData.TickHeal` is the
  heal-side counterpart to `TickDamage` (either or both can be set;
  `StatusEffectTracker.Tick` schedules a tick if either is > 0),
  applied via `CharacterStats.Heal` in `TickEffect`.
  **Tick lifesteal**: `StatusEffectData.TickLifestealPercent` — a
  fraction of each tick's actual (post-mitigation) `TickDamage` also
  heals whoever applied the effect, via a normal `Heal()` call on
  *their* `CharacterStats` (so it gets `HealingMultiplier` and healing
  threat like any other heal, distinct from the target taking the
  damage). Needed `DealDamage` to return the mitigated amount it
  actually dealt, instead of just void. E.g. Soul Siphon.
  `StatType.ManaCostMultiplier` (base 1, synced as
  `SyncedManaCostMultiplier`) is applied in `CharacterStats.TrySpendMana`.
  `StatType.DamageMultiplier` scales all damage a player deals (applied
  in `DealDamage` via the attacker's stats, so DoT ticks count too). Put
  new combat features (combat log, downed state, damage numbers) here,
  not at call sites.
- **Status effects**: `StatusEffectData` asset = `Id`, `DisplayName`,
  `Duration`, `StackingMode`, `TickDamage`/`TickInterval` (0 = no DoT),
  `List<StatBonus>` modifiers (same `StatBonus` struct gear uses;
  applied as `StatModifier`s with the asset as source). Runtime rules
  are in the pure-C# `StatusEffectTracker` (time passed in; tested).
  Different assets always stack independently; ticks catch up after a
  stall; callbacks run outside the dictionary walk so a tick that kills
  the target (→ `RestoreFull` → `ClearAll`) is safe.
  `EffectStackingMode` governs what happens when the *same* effect asset
  is reapplied, keyed internally by a `(data, casterId)` struct rather
  than the asset alone — for two of the three modes `casterId` is
  pinned to 0 so every caster collides on one shared slot, which is
  what makes them "one instance" at all:
  - `RefreshExtendOnly` (default — Burn/Slow/the aura effects): one
    shared instance; reapplying only ever **extends** expiry, never
    shortens it, and never touches the tick schedule (a due tick still
    fires even at the exact moment of a refresh).
  - `Override` (`EffectOneForAll`): also one shared instance, but a
    reapplication **always wins outright** — new duration, new caster
    attribution — regardless of what was left on the old one. If caster
    B casts something Override-mode onto a target caster A already has
    it on, B simply takes over (`AttackerClientId` flips to B).
  - `StackPerCaster` (`EffectRejuvenation`, `EffectEverlivingTouch`):
    each caster's application is a genuinely separate `ActiveEffect`
    (distinct dictionary key), so two different players' heal-over-times
    on the same target both tick independently — recasting by the
    *same* caster still just extends their own instance, per the
    `RefreshExtendOnly` rule.
  - `StackUpToLimit` (`EffectArmorbreakerSunder`): one shared instance
    like `RefreshExtendOnly` (the whole stack shares one timer — a
    reapplication always resets it, doesn't add a second timer), but
    each reapplication up to `MaxStacks` also fires
    `StatusEffectTracker.StackAdded`, which adds another copy of
    `Modifiers` — so magnitude scales with stack count instead of just
    refreshing duration. `ActiveEffect.StackCount` tracks how many
    copies are currently live; `HandleEffectExpired`'s blanket
    `RemoveAllModifiersFromSource` still strips every copy at once
    regardless of count, since they all share the same source
    (`effect.Data`).
  **Known, deliberate limitation, not fixed**: `CharacterStats
  .ActiveEffects` (the client-visible `NetworkList` used for the HUD)
  and `HandleEffectExpired`'s `RemoveAllModifiersFromSource` both still
  key by `Data`/`Id` alone, so two simultaneous `StackPerCaster`
  instances of the same effect only ever show **one** HUD entry, and if
  a `StackPerCaster` effect ever carried `Modifiers` too, one instance
  expiring would wrongly strip the other's stat bonus — inert today
  since neither `StackPerCaster` effect has `Modifiers`, but would need
  fixing before one did. `CharacterStats` mirrors active effects into a
  `NetworkList<ActiveEffectNet>` (`Id` + expiry in `ServerTime`) purely
  for UI; `PlayerHUD` shows "Burning 2.3s" under own bars and the target
  frame. Icebolt's direct hit uses `AbilityData.DirectHitEffectDuration`
  (5s) vs. the patch's `Effect.Duration` (3s).
- **Character collision**: players and mobs live on physics layer 8
  **Characters** (`TagManager.asset`), and Characters↔Characters is off
  in the Physics collision matrix (`DynamicsManager.asset`), so
  characters walk through each other while still colliding with
  everything on Default (terrain, props). `Player.prefab` and
  `MobNPC.prefab` roots carry the layer; mob variants inherit it. New
  character prefabs must be put on Characters too.
- **Regen is discrete, not continuous**: every `regenTickInterval` (5s,
  on `CharacterStats`) the character gains `rate × 5` in one step; the
  dead don't regen. Rates stay defined in per-second units so gear/aura
  bonuses read naturally against the base rate even though they're only
  ever paid out in 5s lumps.
- **Synced derived stats**: `Stat` modifiers (gear, effects) only exist
  server-side, so `CharacterStats` mirrors `MaxHealth`/`MaxMana`/
  `RunSpeed` into `SyncedMaxHealth`/`SyncedMaxMana`/`SyncedRunSpeed`
  `NetworkVariable`s each server `FixedUpdate` (compare-then-write). **UI
  and owner movement must read the `Synced*` values**, never `Stat.Value`
  on a client.
- **Gear**: `CharacterEquipment` re-syncs on `MainMenu.Closed`; only the
  first server-side application calls `RestoreFull()`, later swaps
  `ClampToMax()` (no free mid-fight heal). Wrong-slot items are rejected
  server-side. `CharacterStats.GetStat(StatType)` is the shared stat
  lookup.
  - **Server-wide item uniqueness**: at most one connected player may
    have a given item Id equipped at a time — `CharacterEquipment
    .globalItemOwners` (`static`, server-only, keyed by item Id) is
    claimed in `SetGearServerRpc` and released whenever that item
    leaves a slot (`Equip`) or its owner disconnects
    (`OnNetworkDespawn`). Losing the race just silently drops that item
    from the requester's loadout, the same way an item in the wrong
    slot already does — there's no client-side awareness of who else
    holds what, so the Gear menu can't warn you before you try, and if
    you lose the race your menu will keep showing it equipped locally
    (from `Profile`) until you reopen the Gear page after the rejected
    sync.
- **Enemy targeting**: `TargetingMode` (`Proximity`/`HighestThreat`/
  `LowestThreat`/`FarthestPlayer`) and the pure `TargetSelector.Select`
  live in `Scripts/Enemy/TargetSelector.cs`; `EnemyAI` just builds
  `TargetCandidate`s (threat, distance) from connected alive players.
  Whether a mob participates in threat is purely "does it have a
  `ThreatTable` component" (ogres yes, goblins no).
  - **Healing threat**: `CharacterStats.Heal` also generates threat —
    15% of the amount actually healed (after `HealingMultiplier`), for
    both an instant heal and each individual HoT tick (both funnel
    through this one method, so every healing ability picks it up
    automatically). A heal never hits one specific mob, so there's no
    single `ThreatTable` to credit — `GenerateHealingThreat` scans every
    `ThreatTable` in the scene and adds threat for the healer on every
    mob that **already has the healed character in its own table** (i.e.
    every mob currently fighting them). Gated on
    `healerClientId != NoAttacker`, which excludes aura-pulsed healing
    entirely (same reasoning `HealingMultiplier` uses).
  - **Mana orb drops**: every mob has a flat 5% chance
    (`EnemyAI.ManaOrbDropChance`) on death to spawn a `ManaOrb`
    (`Scripts/Enemy/ManaOrb.cs`) at its death position — a
    trigger pickup that restores 250 mana (`CharacterStats.RestoreMana`,
    a flat add with no `HealingMultiplier`, since it isn't healing) to
    the first player who touches it, then despawns itself. The prefab is
    loaded via `Resources.Load<GameObject>("Prefabs/ManaOrb")`
    (`ManaOrb.TrySpawn`) rather than wired per-mob-prefab, so every
    current and future mob picks it up automatically — the prefab still
    needs Editor setup, see "Not yet done".
- **Menus / dev UI** (`Scripts/UI/`, all IMGUI `OnGUI`, deliberately
  disposable — don't invest in it; real UI should be UI Toolkit): pregame
  `MainMenu` (Choose Skills / Choose Gear / Options / Enter Testing Area)
  and, after `TestingAreaGate.Entered`, the same panels as an **Escape
  menu** (`MainMenu.IsOpen`). The Gear page is a paper-doll: 3×5 grid of
  equipment slots on the left, inventory grid (every unequipped item in
  the game, no real inventory yet) on the right; items are an "X"
  placeholder with a hover tooltip until there's 2D art. While open,
  `PlayerMovement`, `PlayerCamera`, `PlayerTargeting`, `PlayerAbilities`
  ignore gameplay input; on close `MainMenu.Closed` triggers
  loadout/gear re-sync. Options page: UI scale (−/+ 25% steps, 75–250%)
  and movement rebinding (any non-mouse key; binding a key steals it
  from other movement actions *and* ability slots, and vice versa).
  Ability hotkeys are limited to 1–5, Shift+1–5, F1–F5, Q/E/R/T/F/G.
  `DevGui.Begin()` (UI scale) must be the first line of every `OnGUI`,
  laying out against `UIScale.Width/Height`. The Escape menu also has a
  **Summon Mobs** page (`PlayerSummon` on the local player object does
  the spawning; the menu just drives it). **No text fields in in-game
  panels**: IMGUI's native Tab focus traversal moves keyboard focus into
  any focusable control even when the Tab event is `Use()`d, and Tab is
  the tab-targeting key — the summon count is −/+ buttons for exactly
  that reason. The only text field is the server-address box, which is
  gone once connected.
- **Party frames** (`Scripts/UI/PartyFrames.cs`, drawn from `PlayerHUD`,
  top-right): a health+mana row for every *other* connected player,
  WoW-party-style. No party system exists — this simply lists every
  player with a `PlayerMovement` (all of them, since it's a shared open
  lobby). Ordering is identical on every client with no synced state at
  all: sort by `OwnerClientId` (server-assigned, already known
  identically everywhere via each `CharacterStats`'
  `NetworkBehaviour.OwnerClientId`), label by that sorted position
  ("Player 1", "Player 2", …) — that's `Slot.PartyNumber`, a *stable
  identity* every viewer agrees on. Each viewer's own entry is skipped —
  not left as a blank row — so the remaining rows stack up from the top
  with no gap. `CurrentHealth`/`CurrentMana`/`SyncedMaxHealth`/
  `SyncedMaxMana` are already `NetworkVariable`s readable by everyone,
  so this is pure client-side rendering — no new syncing needed.
  - **F1–F5 party targeting** (fixed, not rebindable): `PlayerTargeting`
    targets whoever is drawn in that *row* on the viewer's own screen
    (F2 = second row), via `PartyFrames.GetDisplayOrder`'s returned
    order — **not** the same as `PartyNumber`. Row index is
    viewer-relative (depends on which entry got skipped for being
    "you"); `PartyNumber` is the same for everyone regardless of who's
    watching. Example: canonical order P1,P2,P3,P4 — P2's screen shows
    rows [P1, P3, P4] labeled "Player 1"/"Player 3"/"Player 4"; P2's F2
    hits row 1 → P3, even though P3's own label says "Player 3", not
    "Player 2". **Known conflict, not resolved**: F1–F5 are also
    selectable ability hotkeys in `MainMenu.AllowedKeyBindings` — a
    player who binds an ability there will trigger both the ability and
    party-targeting on the same press.
- **Minimap** (`Scripts/UI/Minimap.cs`, drawn from `PlayerHUD`): circular
  radar bottom-right, north-up, player at centre with a heading tick.
  Compass letters (N/E/S/W) are drawn at fixed screen positions just
  inside the rim, tied to the same fixed world axes the map's north-up
  orientation uses (+Z = N, +X = E) — since the map never rotates to
  face the player, these need no per-player state and are identical for
  everyone by construction. **Blank by default**: blips only draw for
  what the local player's equipped gear reveals (`ItemData.Reveals`,
  `MinimapReveal` flags Players/Mobs, unioned across worn items, read
  client-side from the profile). Revealed `Targetable`s within
  `Minimap.WorldRadius` (150) draw as blips — players green (turning
  yellow on your current target), mobs always red. Two faint reference
  rings are drawn at the 50 and 100 marks so distance reads at a glance.
  **Players reveal is live**, but **Mobs
  reveal is a pulse, not a tracker**: `PlayerHUD` snapshots every mob's
  position every `MobPingInterval` (5s) into `mobPingPositions`
  (`List<Vector3>`, frozen, not the mob's live transform), fading the
  dots out over `MobPingFadeDuration` (4s) before the next pulse, so
  there's a ~1s blind gap each cycle; `Minimap.Draw` takes
  `mobPingPositions`/`mobPingAlpha` alongside the live `blips`/`reveals`.
  The **Echolocation** aura spell grants the Mobs reveal, always on once
  cast. The reverse direction is `ItemData.BroadcastsLocation` →
  server-written `CharacterEquipment.BroadcastsLocation` NetworkVariable
  on the wearer; allies' minimaps draw a broadcasting player regardless
  of their own reveals (**Transmitting Beacon**, Ring1). **Gear slots:
  12** (`GearSlot` — no Belt, and `Ring1`/`Ring2` only, no Ring3/4).
  Rings are **interchangeable**: a ring item's `Slot` is just the
  `Ring1` category, `GearSlotExtensions.IsRing()` treats either physical
  slot as valid for it in `CharacterEquipment.SetGearServerRpc`'s
  placement check, and `MainMenu.TargetSlotFor` picks whichever physical
  ring slot is free (Ring1 first) when equipping one from the inventory
  grid. `GearSlot.Legs` is pinned to its old underlying value (`= 6`)
  so removing Belt didn't shift every later slot's serialized value out
  from under existing item assets. No terrain by design. Disc/blip
  textures are generated at runtime.
- **Controls**: W/S forward/back (both mouse buttons also = forward), A/D
  strafe, Space jump, `\` auto-run (cancelled by W/S or opening the
  menu), right-drag turns the body, left-drag free-looks the camera,
  left-click / Tab targets, `` ` `` self-targets (fixed, not rebindable,
  same as F1–F5 party targeting), Escape clears the target first and
  opens the menu only when nothing is targeted.
  **Right-clicking a mob** (a click, not a drag) targets it and arms
  **auto-attack** (`PlayerAutoAttack`; **T** toggles it on/off for the
  current target too — `MovementAction.AutoAttack`, rebindable on the
  Options page): the server swings the equipped MainHand item's
  `WeaponData` (or the `Fists` fallback wired on the prefab) every
  `SwingInterval` while the target is within that weapon's own `Range`
  and inside the facing cone, stays armed while closing distance,
  follows Tab target changes, and disarms on untarget/death. Each
  weapon sets its own `Range` (`WeaponData.Range`, default 2 for melee —
  `BasicAttackRange` is the fallback constant); Hunter's Bow is the
  first ranged weapon. `ItemData.Weapon` links a MainHand item to its
  weapon; `Fists` is the unarmed fallback when nothing's equipped.
  `StatType.ThreatMultiplier` scales all threat an attacker generates
  (applied in `CharacterStats.AddThreat`) — gear can grant it.
- **Testing lobby scope** (still placeholders, not the real designs):
  instant respawn at map centre, `PlayerSummon` (Escape menu → Summon
  Mobs, spawning `MobGoblin`/`MobOgre` variants on a `summonRadius`
  (100) ring centered on the summoning player's own position, not the
  map), no wipe/reset encounter model, no loot, no unlocks.

## Asset layout

`Assets/Prefabs/{Player,Mobs,Mobs/Visuals,Projectiles,Patches}`,
`Assets/Materials`, `Assets/Animation`, `Assets/Resources/Data/*`,
`Assets/Data/Weapons`, `Assets/Scenes/SampleScene.unity`, `Assets/Settings`
(URP), `Assets/External` (used third-party subset). Root-level
`DefaultNetworkPrefabs.asset`, `New Terrain.asset` etc. are Unity-managed.
`human_readable/` has hand-maintained snapshots (`list_of_all_items.md`,
`list_of_all_spells.md`) — update these whenever content is added,
renamed, or retuned.

## Networking / hosting

- Dedicated Linux server on a VPS (`SERVER_INFO.md`, gitignored, has the
  details and a hard-won-lessons section: `ServerListenAddress` loopback
  default, `UNITY_SERVER` being defined in the Editor, Git-Bash `scp -r`
  silently dropping files). Server runs via manual `nohup`, not systemd.
  The scene's `UnityTransport` is authored with the VPS IP (what a
  shipped build dials by default); `NetworkBootstrap`'s panel has an
  address field that defaults to `127.0.0.1` in the Editor (incl. MPPM
  virtual players) and remembers the last address in the profile.
- A Windows Standalone build was sent to the user's brother and works
  end-to-end against the VPS.

## Not yet done

1. systemd unit for the dedicated server, and **redeploy a fresh server
   build** — the VPS still runs an old build, whose network protocol no
   longer matches (NetworkVariables/RPC signatures have since changed).
2. Fire/ice ground patches as URP Decal Projectors (visual only; needs
   the Decal renderer feature added to the three URP renderer assets in
   the Editor first) and real particle VFX instead of coloured discs.
3. The designed systems in `DESIGN_IDEAS.md` / `ARCHITECTURE_NOTES.md`:
   loot, account-backed unlock-gated profile, encounter wipe/reset state
   machine, taunt/threat reset on combat end.
4. Consider downsizing the largest TriForge textures in `Assets/External`
   (several 50–100 MB 4K PNGs) — LFS is ~1.1 GB, near GitHub's free tier.
5. **Mob pathfinding via NavMesh** — `EnemyAI` currently steers straight
   at its target and runs into walls. Plan: add `com.unity.ai.navigation`
   to the manifest, rewrite mob steering to follow `NavMesh.CalculatePath`
   corners with the existing `CharacterController` (straight-line chase
   as fallback when no path exists); the user adds a `NavMeshSurface` to
   the terrain and bakes in the Editor (rebake after terrain/prop
   changes). Not started.
6. **Earthen Bastion's wall prefab** — the gameplay logic is built
   (`PlayerAbilities.ResolvePersistentStructure`, `PlacedStructure`, the
   ability asset) but `AbilityEarthenBastion.StructurePrefab` is null,
   so the spell currently just fizzles with "Structure not configured
   yet". Editor steps: (1) create a Cube GameObject, (2) add a
   `NetworkObject` component, (3) add `PlacedStructure`
   (`Scripts/Abilities/PlacedStructure.cs` — its
   `RequireComponent(BoxCollider)` adds the collider automatically,
   leave it as a non-trigger), (4) save as a prefab, (5) register it in
   `DefaultNetworkPrefabs.asset` like every other spawned prefab, (6)
   drag it onto `AbilityEarthenBastion`'s `StructurePrefab` field. No
   script changes needed once that's done.
7. **Arctic Winds' zone prefab** — same situation as #6:
   `AbilityArcticWinds.FollowingZonePrefab` is null, so the spell
   currently fizzles with "Zone not configured yet". Editor steps: (1)
   create a GameObject (a Sphere if you want it visible, or empty for
   an invisible trigger), (2) add a `NetworkObject` component, (3) add
   `FollowingZone` (`Scripts/Abilities/FollowingZone.cs` — its
   `RequireComponent(SphereCollider)` adds the collider automatically;
   `Initialize` sets its radius directly, no scaling needed), (4) save
   as a prefab, (5) register it in `DefaultNetworkPrefabs.asset`, (6)
   drag it onto `AbilityArcticWinds`'s `FollowingZonePrefab` field.
8. **Mana orb pickup prefab** — the drop-chance roll and pickup logic
   are built (`EnemyAI.HandleDeath`, `ManaOrb.cs`), but nothing exists
   yet at `Resources/Prefabs/ManaOrb`, so `ManaOrb.TrySpawn` currently
   just logs a warning and skips the drop. Editor steps: (1) create a
   GameObject (whatever visual you want for the orb, or a placeholder
   primitive), (2) add a `NetworkObject` component, (3) add `ManaOrb`
   (`Scripts/Enemy/ManaOrb.cs` — its `RequireComponent(SphereCollider)`
   adds the collider automatically; set it to `isTrigger`), (4) save it
   as a prefab at exactly `Assets/Resources/Prefabs/ManaOrb.prefab`
   (the path `Resources.Load` looks up — no field to wire it to), (5)
   register it in `DefaultNetworkPrefabs.asset` like every other spawned
   prefab.

## Notes for future sessions

Treat the above as established direction. `DESIGN_IDEAS.md` is game
design, `ARCHITECTURE_NOTES.md` is how the code must be shaped to serve
it, `BOSS_DESIGN.md` is encounter design.
