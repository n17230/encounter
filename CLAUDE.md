# encounter

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
  `RespawnClientRpc`), the owner executes via
  `PlayerMovement.TeleportTo` → `NetworkTransform.Teleport` (only the
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
  - **Client-side cast prediction**: `PlayerAbilities` pre-checks
    cooldown / mana (`CurrentMana` NetworkVariable) / target / range /
    facing locally and shows the reason instantly ("Out of range", …),
    starts the cooldown on key press (`predictedCooldownReady`, drives
    the new bottom-centre 8-slot ability bar with a cooldown sweep), and
    the server's start-of-cast rejections come back as
    `NotifyCastRejectedClientRpc(abilityId, reason)` which rolls the
    predicted cooldown/cast bar back. Cooldowns are otherwise never
    synced — the prediction is the client's only view of them.
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
  auras don't stack. `GearManaAmulet` (Necklace) radiates `mana_aura`
  (+1.5 mana/s) at 40 range. **`StatType.ManaCostMultiplier`** (base 1,
  synced as `SyncedManaCostMultiplier`) is applied in
  `CharacterStats.TrySpendMana`; `GearStaff` (MainHand, 20 dmg / 2 s
  basic attack) gives −10%. Put new combat
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
  (`GearSword` → `WeaponSword`, 40 dmg / 2 s; Fists are 15 dmg / 1.5 s).
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

## Notes for future sessions

Treat the above as established direction. `DESIGN_IDEAS.md` is game
design, `ARCHITECTURE_NOTES.md` is how the code must be shaped to serve
it, `BOSS_DESIGN.md` is encounter design. The refactor on 2026-09-11 was
done on branch `refactor/architecture` and merged to `main`.
