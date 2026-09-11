# reallyfungame

A 3D multiplayer game, built in Unity, meant to be hosted on a server so the
user and friends can play together.

## Project state (as of 2026-09-09)

- Unity Editor: **upgraded to Unity 6.3 LTS** (`6000.3.23f1`, confirmed in
  `ProjectSettings/ProjectVersion.txt`) — migration from 2022.3.21f1 is
  **done**. Reason for the move: Multiplayer Play Mode
  (`com.unity.multiplayer.playmode`), needed to test multiple clients
  in-editor, requires Unity 2023.1+ and doesn't support 2022.3 at all.
  Chose the LTS stream over the "6.6 (recommended)" Tech Stream Unity Hub
  also offers, for the same long-term-support reasoning as the original
  2022.3 choice.
  - Note on naming: what package registries/docs call "6000.3" (dropping
    the leading zeros: "6000.3" → "6.3") is the same thing Unity Hub
    displays as **"Unity 6.3"**.
  - Opening the project in the new Editor auto-resolved the package
    upgrade cleanly (see below) — no manual intervention was needed
    beyond installing the Editor and opening the project.
- Render pipeline: **URP** (`com.unity.render-pipelines.universal`),
  auto-upgraded by the Editor to **17.3.0** (the Unity 6-line version) on
  first open. Chosen over Built-in for performance and because it's the
  actively supported pipeline.
- Product name renamed to **"reallyfungame"** in Player Settings
  (`ProjectSettings/ProjectSettings.asset`).
- **Netcode for GameObjects 2.13.2**, **Unity Transport 2.7.4** (bumped
  from 2.6.0 by the Editor), and **Multiplayer Play Mode 2.0.2** are
  installed and resolved in `Packages/manifest.json`. The Editor also
  auto-added `com.unity.multiplayer.center` (a companion package) — left
  in place, no reason to remove it.
- **Git repository initialized**, with a Unity-appropriate `.gitignore`
  (`Library/`, `Temp/`, `Logs/`, `UserSettings/`, build output, etc.) and
  Git LFS configured (`.gitattributes`) for binary asset types (images,
  models, audio, fonts). Nothing has been committed yet.
- **Networked movement + camera prototype proven working**, tested via
  Host + a Multiplayer Play Mode virtual player acting as Client — both
  players move, are visible to each other, and are affected by gravity/
  the floor collider. Built in `Assets/Scenes/SampleScene.unity`:
  - `NetworkManager` object: `NetworkManager` + `UnityTransport` (wired
    into the Network Transport field) + `NetworkBootstrap` (our script,
    on-screen Host/Client/Server buttons for testing) + the `Player`
    prefab set as Default Player Prefab.
  - `Player.prefab`: Capsule with `NetworkObject`, `NetworkTransform`,
    `CharacterController` (Capsule Collider disabled), `PlayerMovement`
    (server-authoritative — owner sends input via `ServerRpc`, only the
    server calls `CharacterController.Move`, `NetworkTransform`
    replicates the result; also handles jump — Spacebar sets a
    latched `serverJumpRequested` flag that's only cleared once
    consumed, so a jump input from `Update()` is never lost between
    physics steps even if it arrives on a frame the server doesn't
    immediately process), and `PlayerCamera` (owner-only third-person
    camera: a `CameraPivot` child handles local, unsynced pitch from
    mouse Y while right-click is held, yaw comes for free from the
    parented hierarchy following the server-authoritative character
    rotation; disables the scene's default Main Camera and enables its
    own only for `IsOwner`).
  - A floor plane was added to the scene (default Mesh Collider, no
    Netcode components needed — static level geometry is already
    identical on host and client since both load the same scene).
    Resized upward several times during testing, currently scale
    `(5, 1, 5)` (50×50 world units, up from an original 10×10).
    `PlayerSummon.mapHalfExtent` (see below) must be kept matching this
    size — currently `25`.
  - Scripts live in `Assets/Scripts/Player/` (`PlayerMovement.cs`,
    `PlayerCamera.cs`) and `Assets/Scripts/Network/`
    (`NetworkBootstrap.cs`).
  - Known simplification, not yet addressed: no client-side prediction —
    the owner's own movement/turning has visible input lag equal to
    round-trip time to the server, since the owner doesn't move locally
    until the server's authoritative result replicates back. Fine for
    local/prototype testing; worth revisiting once real network latency
    (VPS hosting) is in the picture.
- **Character stat system built and wired in** (`Assets/Scripts/Stats/`):
  `Stat` (base value + flat/percent modifiers, cached/recalculated) and
  `StatModifier`, plus `CharacterStats` (NetworkBehaviour on `Player`)
  holding MaxHealth/HealthRegenRate/MaxMana/ManaRegenRate/RunSpeed/Armor
  as `Stat`s, with `CurrentHealth`/`CurrentMana` as server-authoritative
  `NetworkVariable<float>`s, server-side regen tick, and
  `ApplyDamage`/`Heal`/`TrySpendMana` methods. `PlayerMovement` now reads
  run speed from `CharacterStats.RunSpeed` instead of its own field.
- **Targeting system built**: `Targetable` (marker component + display
  name + cached `CharacterStats` ref, on `Player`) and `PlayerTargeting`
  (owner-only left-click raycast against the owner's own camera —
  `PlayerCamera.Camera`, not `Camera.main`, since that's unreliable with
  multiple per-player cameras in the scene — sets `CurrentTarget`,
  purely client-local for now, not synced to the server).
- **Simple dev HUD** (`PlayerHUD`, `OnGUI`-based, no Canvas): health/mana
  bars bottom-left, current target name + health top-left. Deliberately
  minimal/placeholder — not real UI art.
- **Ability system + two working spells (Firebolt, Icebolt) + ground
  patches + a debuff system, all proven working**, tested via Host +
  Multiplayer Play Mode Client and (as of 2026-09-09) against the real
  VPS-hosted dedicated server:
  - `AbilityData` (`Assets/Scripts/Abilities/`): ScriptableObject base
    type — AbilityName, RequiresTarget, Range, Cooldown, CastTime,
    ManaCost, ThreatValue (not wired to anything yet, no threat system
    exists), Damage, MissileSpeed, ProjectilePrefab, plus ground-patch
    fields (GroundPatchPrefab, Min/MaxPatchCount, PatchScatterRadius,
    PatchRadius, PatchDuration) and debuff fields (Debuff: DebuffType,
    DebuffMagnitude, DebuffTickInterval, DebuffDuration).
  - `DebuffType` enum (`None`/`Burn`/`Slow`).
  - `CharacterStats` gained a full debuff system: `ApplyDebuff(type,
    magnitude, tickInterval, duration)` — refreshing an already-active
    debuff of the same type only extends its expiry, never stacks or
    double-adds a modifier; `Burn` ticks damage on its own independent
    schedule (`NextTickTime`) that a refresh never touches (this is
    specifically what guarantees a due damage tick always fires even if
    a refresh lands at the same moment); `Slow` adds a
    `PercentAdditive` `StatModifier` on `RunSpeed` once, removed when
    the debuff actually expires.
  - `Projectile`: on impact, applies both direct damage AND
    `ApplyDebuff` to the target (getting hit by a bolt now inflicts the
    debuff directly, not just standing in a patch), then spawns
    `MinPatchCount`–`MaxPatchCount` `GroundPatch` instances scattered
    randomly (`Random.insideUnitCircle * PatchScatterRadius`) around the
    impact point.
  - `GroundPatch`: a trigger volume (`SphereCollider`) whose visual
    scale and collider radius are both derived from the same
    `PatchRadius` value at spawn time (`Initialize` scales
    `transform.localScale` and sets the collider's local radius to
    Unity's default Cylinder mesh radius `0.5`, so they can never
    visually/functionally mismatch regardless of the prefab's authored
    scale). While a player stands in it, refreshes their debuff once per
    second. Its own lifetime (`PatchDuration`, how long the hazard
    persists — 6s) is a separate clock from the debuff's lifetime
    (`DebuffDuration`, how long the effect lasts on a player — 3s).
  - `PlayerAbilities`: holds an `abilityPool` (`List<AbilityData>`) —
    every ability that exists in the game, in a fixed order shared with
    `MainMenu`'s `availableAbilities` array (currently `[0]=Firebolt,
    [1]=Icebolt`). At runtime, key bindings and slot assignment come
    from `LoadoutSelection` (see Main Menu / kit system below), not a
    prefab-baked list — the owner client checks its own
    `LoadoutSelection.SlotAbilities`/`SlotKeys` each frame, and on cast
    sends the server a slot index; the server resolves that to a pool
    index via `SetLoadoutServerRpc` (sent once on spawn) and looks up
    cooldowns per pool index. This exists because `AbilityData`
    ScriptableObjects can't be sent directly over an RPC — only the
    shared pool index can.
  - **Facing-cone requirement**: casting requires the target be within a
    60° horizontal cone centered on the caster's forward direction
    (`facingConeAngle` on `PlayerAbilities`, yaw-only — pitch/camera
    angle doesn't matter, confirmed intentional). Checked both when the
    cast *starts* and again when it *resolves* (`IsWithinFacingCone`
    called in both `CastAbilityServerRpc` and `ResolveAbility`) — the
    target is allowed to leave the cone mid-cast while charging, it just
    has to be back in the cone at the moment the cast fires. Line of
    sight (`Physics.Linecast`, ignoring the caster's/target's own
    colliders) is checked only at resolve time.
  - **Cast bar**: `PlayerAbilities` draws a simple `OnGUI` progress bar
    centered at the bottom of the screen while `isCasting`, showing the
    ability name and elapsed/`CastTime` progress. Owner-only, no
    Canvas/UI Toolkit — same "plain OnGUI" style as the rest of the dev
    UI in this project.
  - **Target ring indicator** (`TargetRingIndicator`, on `Player`,
    requires `PlayerTargeting`): draws a bright yellow ring under
    whatever the *local* player is currently targeting. Procedurally
    generates an annulus mesh at runtime (no texture/image asset
    needed) with an unlit URP material (`_Cull` off). Only created for
    `IsOwner` in `OnNetworkSpawn` and is never networked — by design,
    each player only ever sees their own target ring, never anyone
    else's.
- **Main menu / kit system** (`Assets/Scripts/Network/MainMenu.cs`):
  pregame gate before `NetworkBootstrap`'s Host/Client/Server buttons
  appear (`TestingAreaGate.Entered`, static bool). "Choose Kit" opens a
  panel listing `availableAbilities`; clicking one assigns it to the
  next open slot in an 8-slot loadout (`LoadoutSelection`, static —
  `SlotAbilities[8]`/`SlotKeys[8]?`); an ability already in a slot is
  hidden from the picker, enforcing a unique set. Clicking an assigned
  slot reveals "Remove" (clears the slot) and "Set Key Binding" (enters
  capture mode — the next allowed key pressed binds to that slot,
  stealing the binding from any other slot that had it). Allowed keys
  are restricted to `Alpha1-5`, `Shift+Alpha1-5`, `F1-F5`, `Q`, `E`,
  `R`, `T`, `F`, `G` (`KeyBindingOption`, `BuildAllowedKeyBindings`).
  "Enter Testing Area" sets the gate; `PlayerAbilities.OnNetworkSpawn`
  (owner only) then syncs the chosen loadout to the server. Still not
  the real persistent/unlock-gated 10-slot loadout from
  `DESIGN_IDEAS.md` — this is a testing-lobby scoped first version of
  that idea, and `LoadoutSelection`/`TestingAreaGate` reset only on
  process restart (static, not saved anywhere).
  - **Confirmed done in the scene** (2026-09-09): `SampleScene` has a
    `MainMenu` GameObject with `Available Abilities` = `[AbilityFirebolt,
    AbilityIcebolt]` and `Available Gear` = `[GearShield]`, matching
    `Player.prefab`'s `abilityPool`/`CharacterEquipment.itemPool` order.
    The skills screen lays out "Available Abilities" and "Your Kit" as
    two side-by-side columns inside a scroll view rather than stacked;
    see "Menu restructured into three separate buttons" below — the
    combined single-panel "Choose Kit" design was since split into
    separate Choose Skills / Choose Gear screens.
- **Gear/equipment system, first version** (`Assets/Scripts/Items/`):
  `GearSlot` enum (15 slots per `DESIGN_IDEAS.md` — Helmet, Necklace,
  Chest, Cape, Gloves, Belt, Legs, Boots, Ring1-4, Trinket, MainHand,
  OffHand), `StatType` enum (which `CharacterStats` stat a bonus
  targets), `ItemData` ScriptableObject (`ItemName`, `Slot`, a
  `List<StatBonus>` where each bonus is `{Stat, ModifierType, Value}`
  reusing the existing `Stat`/`StatModifier` types directly). Same
  pregame-menu pattern as the kit system: `GearSelection` (static,
  `ItemData[]` indexed by `GearSlot`) holds the client's picks in the
  "Gear" section of the Choose Kit menu (click an empty slot's item
  button to equip, "Unequip" to clear); `CharacterEquipment` (on
  `Player`, mirrors `PlayerAbilities`' `abilityPool` design — holds an
  `itemPool` of every item in the game in a fixed order shared with
  `MainMenu`'s `availableGear`, since `ItemData` can't cross an RPC
  either) sends the server a slot→pool-index mapping once on spawn;
  the server applies/removes each item's `StatBonus`es as
  `StatModifier`s (source = the `ItemData` asset itself, so
  `RemoveAllModifiersFromSource` cleanly strips a specific item's
  bonuses on unequip/re-equip) and calls `CharacterStats.RestoreFull()`
  once gear is applied so max-health/mana bonuses take effect
  immediately rather than waiting on the next regen tick.
  - First item: `Assets/GearShield.asset` — "Shield", `OffHand` slot,
    `+20 Armor` (Flat). Given `CharacterStats.ApplyDamage`'s existing
    mitigation formula (`1 - Armor/100`), this is exactly "reduce
    incoming damage by 20%" as requested, with no new damage-mitigation
    code needed.
  - Same caveats as the kit system: testing-lobby-scoped, static
    (resets on process restart), not the persistent/unlock-gated real
    system from `DESIGN_IDEAS.md`.
  - Assets: `Assets/FireBolt.prefab` / `Assets/IceBolt.prefab`
    (projectiles, each with a `TrailRenderer` and their own tinted+
    emissive material — `MaterialFire`/`MaterialIce`; note: setting
    emission by hand-editing the `.mat` YAML directly doesn't stick,
    Unity resets `m_ValidKeywords` on next Inspector touch — must be set
    via the Inspector's own Emission checkbox to persist),
    `Assets/PatchFire.prefab` / `Assets/PatchIce.prefab` (flat scaled
    Cylinders, ground patches), `Assets/AbilityFirebolt.asset` (25
    damage, Burn debuff: 5 dmg/sec, 3s duration) /
    `Assets/AbilityIcebolt.asset` (10 damage, Slow debuff: −66% run
    speed, 3s duration) — all registered in
    `Assets/DefaultNetworkPrefabs.asset` where applicable.
  - Known simplification: fire's "trail along the path" was interpreted
    as a purely visual `TrailRenderer` effect, not a hazardous trail of
    burning ground the whole flight path — flag if that was actually the
    intent.
- **First enemy NPC**: `MobGoblin` (`Assets/MobGoblin.prefab` — no
  longer placed in the scene; the original scene instance was deleted
  per user request, it now exists purely as a prefab spawned on demand,
  see `PlayerSummon` below) — reuses `CharacterStats` (`baseMaxHealth
  60`, cut 40% from an original 100 — user found the original too
  tanky; `baseRunSpeed 6.3`, manually tuned down by the user from an
  original `7.2`/"20% faster than a player" — "the goblins were too
  fast"), `Targetable`, `CharacterController`; `EnemyAI` script
  (`Assets/Scripts/Enemy/`) is server-only, naively chases whichever
  connected player is nearest (no threat system yet — pure proximity)
  and melee-attacks for 10 damage every 1.5s once in range (2 units).
  Fully targetable/damageable by player abilities like any other
  `CharacterStats` holder.
- **Death/respawn, explicitly scoped to the open testing lobby only** —
  `CharacterStats` fires a one-shot `OnDeath` event when health hits 0.
  `PlayerRespawn` (on `Player`) respawns the player instantly at the
  map's center `(0, 1, 0)`, fully healed/manaed. **This is a
  placeholder for the open lobby, not the real death/failure model** —
  `DESIGN_IDEAS.md` already specifies actual instanced boss/mob
  encounters use a wipe/reset-the-whole-encounter model instead, which
  hasn't been built yet and is a different system from this.
  `EnemyAI`'s `HandleDeath` does **not** respawn the mob — it just
  despawns (`NetworkObject.Despawn()`, which defaults to destroying).
  Mobs no longer auto-respawn at all; that mechanic was superseded by
  the summon system below.
- **Player-driven mob summon system** (`PlayerSummon`, on `Player`,
  testing-lobby-scoped like the above) — replaces the old goblin
  auto-respawn-at-map-edge idea entirely. Adds an `OnGUI` panel
  (top-right) with a "Summon Mob" toggle; picking a mob from
  `summonableMobs` (currently just `MobGoblin`) and entering a count
  sends `RequestSummonServerRpc(mobIndex, count)`. Server clamps count
  to 1–`maxSummonCount` (20) and instantiates+spawns that many at
  random points on a circle of radius `mapHalfExtent` (25, kept in sync
  with the floor's current size — see above) around the map center.
  Summoned mobs just despawn on death (no respawn) — players summon
  more whenever they want more to fight. `summonableMobs` now includes
  both `MobGoblin` and `MobOgre` (see below).
- **Threat system, first version** (`Assets/Scripts/Enemy/ThreatTable.cs`)
  — as simple as requested: 1 point of threat per 1 point of damage
  actually dealt (post-armor-mitigation). `ThreatTable` is a plain
  `MonoBehaviour` (not networked — server-only bookkeeping, never read
  by clients) holding a `Dictionary<ulong clientId, float threat>`.
  `CharacterStats` caches an optional `GetComponent<ThreatTable>()` in
  `Awake` and, whenever `ApplyDamage`/`ApplyDebuff` is given an attacker
  client ID (see below), calls `threatTable?.AddThreat(...)` — so
  **whether an entity participates in threat at all is entirely
  determined by whether it has a `ThreatTable` component**, not by any
  special-cased logic. `EnemyAI.FindTarget` targets whoever has the
  highest threat against it (ties broken by nearest distance), falling
  back to pure proximity (`FindNearestPlayer`, unchanged) whenever there's
  no `ThreatTable` or no threat entries yet.
  - `MobGoblin` deliberately does **not** have a `ThreatTable` — per
    explicit user decision, goblins stay exactly as before (pure
    nearest-player targeting), reusing the same shared `ApplyDamage`/
    `ApplyDebuff` code path rather than a separate implementation.
  - Attacker attribution required threading a `ulong attackerClientId`
    (sentinel `CharacterStats.NoAttacker = ulong.MaxValue` when absent)
    through every damage path: `PlayerAbilities` passes its own
    `OwnerClientId` for both instant-cast damage and `Projectile.Initialize`;
    `Projectile.Impact` passes it into both `ApplyDamage` and
    `ApplyDebuff`; `GroundPatch.Initialize` also takes a caster ID and
    passes it along each time it refreshes an occupant's debuff; and
    `ActiveDebuff` (inside `CharacterStats`) now stores the attacker so a
    `Burn` tick's damage (fired later, independently, from
    `ProcessDebuffs`) still attributes threat correctly to whoever cast
    the fire spell, not just the initial hit.
- **New creature: `MobOgre`** (`Assets/MobOgre.prefab`), built specifically
  to test the threat system (unlike `MobGoblin`, it **has** a
  `ThreatTable`). Stats relative to the player/goblin baselines per
  explicit request: normal player run speed (`6`, vs. goblin's fast
  `6.3`), 2× goblin's melee damage (`20` vs. `10`, same 1.5s interval),
  150% of the player's max health (`150` vs. `100`). Also 150% scale on
  both width and height (`transform.localScale = (1.5, 1.5, 1.5)`,
  following the same "just scale the whole GameObject uniformly, leave
  `CapsuleCollider`/`CharacterController` radius/height at the mesh
  defaults" pattern `MobGoblin` already used for its 0.6× shrink).
  Distinct tinted material `MaterialOgre.mat`. Registered in
  `DefaultNetworkPrefabs.asset` and added to `Player.prefab`'s
  `PlayerSummon.summonableMobs` alongside `MobGoblin`.
- **Gear/menu tooltips**: hovering an ability or gear item anywhere in
  the main menu (available list, assigned kit slot, or an equipped gear
  slot's label) now shows a popup with its numbers — damage, mana cost,
  cooldown, cast time, range, and debuff for abilities; slot and each
  `StatBonus` for gear. Built on IMGUI's built-in tooltip mechanism
  (`GUIContent(text, tooltip)` + reading `GUI.tooltip` once at the end of
  `OnGUI` to draw a box at the mouse position) rather than any manual
  hover-tracking, so show/hide behavior is automatic and exactly matches
  "goes away when the mouse leaves" with no extra state needed.
- **Menu restructured into three separate buttons**: `MainMenu`'s main
  screen now shows "Choose Skills", "Choose Gear", and "Enter Testing
  Area" as three separate buttons (previously "Choose Kit" opened one
  combined panel with everything in it). Each picker is its own full
  screen with its own Back button. The skills screen still lays out
  "Available Abilities" and "Your Kit" as two side-by-side columns
  inside a scroll view; the gear screen lists all 15 slots. Both panels
  were sized generously enough that, with the current small amount of
  content (2 abilities, 1 gear item), nothing requires scrolling to
  reach — worth re-checking sizes if the abilities/gear pools grow a
  lot.
- **Regen rates cut to a quarter** for both `Player` and `MobGoblin`:
  health regen `2 → 0.5`/sec, mana regen `5 → 1.25`/sec.
- Camera/look sensitivity tuned up from defaults during testing, across
  several rounds of user feedback: `lookSensitivity` (yaw, on
  `PlayerMovement`) = 45, `pitchSensitivity` (pivot pitch, on
  `PlayerCamera`) = 45, camera offset from pivot = `(0, 2.5, -8)`.
- **Movement rebound to plain WASD** (`PlayerMovement.cs`): `W`
  forward / `S` backward (both-mouse-buttons-held still also counts as
  forward), `A`/`D` strafe left/right, explicit `KeyCode` checks rather
  than the legacy Input Manager `Horizontal`/`Vertical` axes. This also
  fixed a latent bug — the server-side field the forward flag was
  written into (`serverMoveForward`) didn't match the one declared
  (`serverForwardInput`), which wouldn't have compiled; forward is now
  a single float (`-1`/`0`/`1`) carried through in `serverForwardInput`,
  enabling backward movement, which didn't exist before. No dedicated
  strafe-only keys (Q/E) were found bound to movement in code — Q/E
  only appear as ability-slot keybinding options in `MainMenu.cs`,
  untouched by this change. `DESIGN_IDEAS.md`'s controls section
  (previously "no backward movement key" by design) updated to match.
- **In-game Escape menu + rebindable movement keys** (`MainMenu.cs`):
  once in the testing area, `Escape` toggles a centered menu (Skills /
  Gear / Keybindings / Resume) that reuses the same pregame Skills and
  Gear pickers. `MainMenu.IsOpen` (static) is checked by
  `PlayerMovement` (sends zeroed input each frame and cancels auto-run
  rather than going silent, since the server holds the last input it
  received), `PlayerCamera` (no mouse-look), `PlayerTargeting` (no
  click/Tab targeting) and `PlayerAbilities` (no casting) so menu
  clicks/keys never leak into gameplay. On close, a static
  `MainMenu.Closed` event makes the owner's `PlayerAbilities`/
  `CharacterEquipment` re-send their loadout/gear to the server (the
  spawn-time sync was refactored into `SyncLoadoutToServer`/
  `SyncGearToServer`). `CharacterEquipment` now only calls
  `RestoreFull()` on the *first* server-side gear application; later
  swaps call the new `CharacterStats.ClampToMax()` instead, so a
  mid-fight re-equip isn't a free full heal. Movement keys live in
  `MovementBindings.Keys` (static `KeyCode[]` indexed by the
  `MovementAction` enum: Forward/Backward/StrafeLeft/StrafeRight/Jump/
  AutoRun, defaults W/S/A/D/Space/Backslash) — the Keybindings page
  captures any non-mouse/non-joystick key; binding a key already used
  by another movement action *or* an ability slot steals it (and vice
  versa when binding an ability key), matching the existing
  ability-slot steal rule. `Escape` while waiting on a key press only
  cancels the capture. The pregame menu has the same button. Same
  static/not-persisted scoping as the rest of the lobby menu.
  - The Keybindings page is now the **Options** page (button renamed in
    both the pregame and Escape menus), with a **UI Scale** control at
    the top (−/+ in 25% steps, 75%–250%, default 100%) above the
    movement keybinds. `UIScale` (static, in `MainMenu.cs`) holds the
    value; **every `OnGUI` in the project must call `UIScale.Apply()`
    first** (sets `GUI.matrix` to a uniform scale) **and lay out
    against `UIScale.Width`/`UIScale.Height` instead of
    `Screen.width`/`Screen.height`** so edge-anchored elements stay on
    the edges — currently done in `MainMenu`, `PlayerHUD`,
    `PlayerAbilities` (cast bar), `PlayerSummon` and `NetworkBootstrap`.
    Any new IMGUI script needs the same two lines or it'll draw at 1×
    and mis-anchor. Deliberately stepped buttons rather than a slider:
    a slider changes the scale under the cursor mid-drag, which makes
    the handle jump.
- **VPS dedicated server provisioned and a real connection proven working
  end-to-end** (2026-09-09) — see `SERVER_INFO.md` (gitignored, has
  access details) for the full setup, credentials, and — importantly —
  a "hard-won lessons" section covering three real bugs hit and fixed
  along the way: `ServerListenAddress` defaulting to loopback-only,
  `UNITY_SERVER` being defined in the Editor too (not just real builds)
  which hijacked local Play-mode testing, and a Windows/Git-Bash `scp
  -r "path\."` recursive-copy that silently failed to transfer some
  files despite exiting cleanly. Worth reading that file before touching
  deployment again. Server is currently running manually (not yet a
  systemd service) — see "Not yet done" below.

## Plan / decisions made so far

- **Networking**: go with **Netcode for GameObjects (NGO)** — official
  Unity package, integrates with GameObject workflow. Add
  **Multiplayer Play Mode** (`com.unity.multiplayer.playmode`) alongside it
  to test multiple clients in-editor without full builds. (This is what
  drove the Unity 6 upgrade above — MPPM doesn't support 2022.3.)
- **Hosting model**: **dedicated server**, not listen-server/relay — the
  user explicitly wants to host on a server they control, not rely on one
  player's machine. Build the Linux Dedicated Server target, run it under
  systemd on a VPS (Hetzner/DigitalOcean, x86_64, ~2GB RAM is enough for a
  friends-scale game). Open UDP 7777 (default Unity Transport port) in both
  the VPS OS firewall and the provider's cloud firewall.
- **Architecture guidance given**: build server-authoritative from the
  start (server owns state, clients send input) rather than prototyping
  single-player and retrofitting networking later — retrofitting is
  effectively a rewrite of every gameplay script.
- **Scope guidance given**: start small — one simple mechanic (tag,
  king-of-the-hill, etc.) working end-to-end over the network before adding
  content.

## Not yet done (next steps)

1. **Set up systemd** for the dedicated server on the VPS (currently just
   a manually-launched `nohup` process — won't survive a reboot or
   restart itself on crash). See `SERVER_INFO.md` for the current manual
   run command to base the service file on.
2. **Fireball VFX polish still outstanding**: the burn/slow mechanics are
   fully built and working (see above), but the *visual* ask from
   2026-09-07 — flaming spots and slow patches using real fire/ice
   particle effects rather than plain colored discs — hasn't been done.
   Also flagged: fire's "trail along the path" was built as a
   `TrailRenderer` only (cosmetic), not a hazardous trail of burning
   ground the whole flight path — confirm that interpretation was right.
3. A way to switch the client's connection target between local dev
   (`127.0.0.1`) and the real VPS without hand-editing the scene each
   time — see `SERVER_INFO.md`.
4. Continue layering in the designed systems from `DESIGN_IDEAS.md` /
   `ARCHITECTURE_NOTES.md` on top of what's built: loot, and the full
   persistent/unlock-gated player loadout and equipment systems (the
   kit/gear pickers and the threat system now have first working
   versions — see above — but are all still testing-lobby-scoped:
   static state that resets on process restart, no unlocks/progression).
   The threat system is also currently only wired up for `MobOgre` —
   any future mob that should use aggro instead of pure proximity needs
   a `ThreatTable` component added, same as `MobOgre` has.
5. **Convert `PatchFire`/`PatchIce` to URP Decal Projectors** so ground
   effects conform to the (now sculpted, non-flat) terrain instead of
   being rigid flat discs that clip into slopes/float over dips. Needs
   the **Decal** Renderer Feature added to all three URP Renderer assets
   first (`Assets/Settings/URP-Balanced-Renderer.asset`,
   `URP-Performant-Renderer.asset`, `URP-HighFidelity-Renderer.asset` —
   via each one's Inspector → Renderer Features → Add Renderer Feature →
   Decal), which is an Editor-only step deliberately left to the user
   rather than hand-edited, since it means writing a new nested sub-asset
   into a core rendering asset's serialized feature list with no way to
   verify the exact format without Unity re-serializing it. Once that's
   done, swap each patch prefab's flat Cylinder mesh for a
   `DecalProjector` component + fire/ice material, sized to match its
   existing `PatchRadius`-derived scale — `GroundPatch.cs`'s actual
   gameplay logic (the `SphereCollider` trigger + tick-damage code)
   doesn't need to change at all, this is visual-only.

## Done, not previously marked as such

- **The brother can connect and play** — a Windows Standalone Player
  build (not Dedicated Server) was made from the project state with
  `NetworkManager`'s Unity Transport already pointed at the real VPS
  (`159.89.232.8:7777`), zipped, and sent to him. He runs the `.exe` and
  clicks the on-screen **Client** button. Verified working end-to-end
  against the real VPS.

## Notes for future sessions

This file was seeded from a conversation in a different working directory
(`C:\dev\game`, which was an empty scratch folder — the actual project
lives here) where the user was walked through Unity Hub setup, template
choice (Universal 3D), and the overall hosting approach. Treat the above as
established direction, not open questions, unless the user revisits them.
