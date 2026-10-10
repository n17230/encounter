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

## Workflow for every request

Every request runs this pipeline, not just "write the code." Skip nothing
unless the request genuinely doesn't touch that stage (e.g. a pure question
has no gate/mutation/review stage to run).

1. **Refine.** Restate the exact ask against Core rules 1–2 before touching
   anything: does it require inventing anything the user didn't specify, or
   touching code outside what was asked? Then check it against the actual
   current codebase (the referenced classes/assets/fields still exist and
   look like what the ask assumes) rather than proceeding on the ask's own
   description of the code. If either check turns up something unclear or
   inconsistent, ask (`AskUserQuestion`) instead of guessing.
2. **Plan.** For anything beyond a trivial/obvious change, use Plan Mode to
   settle the technical approach (which files, what structure, test
   placement) and get sign-off *before* writing code — don't improvise
   architecture mid-implementation. Skip this for genuinely small asks
   (rename, one-line fix, add a test) — proportional, not ceremonial.
   **Plan challenge — three agents finalize the plan before any code is
   written** (runs whenever step 2 produces a plan; skipped with it):
   1. *Devil's advocate* — once the plan is written, and before
      execution, start a fresh, non-fork subagent (`Agent`) given only the
      user's instruction and the plan, not the reasoning behind it. Its job
      is to find problems with the instruction and the plan, and better
      ways to do any part of it.
   2. *Response* — the planning agent reads the devil's advocate's
      findings and answers each one (accept, reject, or modify, with why),
      producing a revised plan alongside the original.
   3. *Neutral agent* — a second fresh, non-fork subagent given the
      instruction, both plans, the critique, and the responses. For each
      specific disputed detail it votes which side is right or wrong, with
      a reason. **It may ask the planning agent and the devil's advocate
      questions whenever it needs to** (`SendMessage`) — both stay
      reachable until it's done — and keeps going back and forth until
      each disputed detail is settled, rather than voting on a guess.
   4. *Finalize — one cohesive plan, written by the neutral agent.* After
      all the discussion, back-and-forth, and Q&A, the neutral agent takes
      everything together and returns a single plan. **Cohesive is the
      requirement**: it is not the original plan with the accepted points
      pasted in. Independently-correct ideas can conflict or break each
      other when combined, so the neutral agent re-reads the whole result
      as one change — checking that the accepted points fit together, that
      nothing a rejected point depended on is left dangling, and that the
      files/structure/tests still make sense as a unit. A point that's
      really the user's call goes to `AskUserQuestion`. Only then present
      that final plan for sign-off (`ExitPlanMode`).
3. **Act (generate).** Implement the minimal diff. Any new pure-C# logic
   (the kind that lives in `Scripts/**` and is unit-testable, per the
   existing `Assets/Tests/EditMode` pattern) gets its test written in the
   same change, not deferred to later.
4. **Quality gate.** Before calling anything done: `dotnet build
   Tools/CompileCheck.csproj` must pass clean; re-check the diff against
   scope (nothing tangential slipped in — if something did, back it out and
   write it down for the user instead of keeping it); any new `.cs`/
   `.asmdef`/folder has a hand-authored `.meta` with a fresh GUID.
5. **Mutation pass (reasoned, not executed) — fresh subagent.** Hand this
   off to a fresh, non-fork subagent (`Agent`, e.g. `general-purpose`),
   given only the diff and the touched test file(s) — not the conversation
   history that produced them. It should hand-trace 2-3 plausible mutations
   (flipped comparison, off-by-one, swapped constant) against the tests and
   report which would and wouldn't be caught. A `fork` doesn't satisfy this
   — it inherits full context, so it shares whatever misunderstanding
   produced the code and tests together. This can't actually execute from
   here — EditMode tests only run from the Editor's Test Runner, and there
   is no real executable test suite to run outside it, by design — so it's
   reasoned coverage, not a passing run.
6. **Independent review — fresh subagent.** Same reasoning as step 5: hand
   the diff to a fresh, non-fork subagent for review rather than treating
   "I wrote it carefully" as the review. Give it the diff and the ask, not
   my reasoning for why it's correct. Report its findings before declaring
   the change done. **The user waits for this review to finish and then
   reviews the code themselves** — so don't hand the change back as
   "done" before it completes, and don't paste code into the CLI for them
   to read (see below).
7. **Manual verification — enumerated, targeted.** Since Play Mode /
   multi-client runs can't be driven from here, hand back a short concrete
   checklist of what to click/test in the Editor for the golden path plus
   one edge case. On top of that baseline, whenever the change touches
   anything this pipeline structurally cannot judge — visual/VFX
   appearance, animation timing, movement/camera feel, balance or tuning
   numbers, or anything needing a hand-authored Editor step (prefab
   `GlobalObjectIdHash`, meshes, materials, per "Not yet done" above) —
   call that out as its own numbered list of specific things for the user
   to check, not folded into the generic checklist. Say plainly that these
   items need the user's own judgment and haven't been (and can't be)
   verified by anything upstream in this pipeline. Never claim "it works"
   without that caveat attached.

**Don't output code changes to the CLI.** Make edits to the files and
report in prose (which files, what changed, why) — no diffs, code blocks
or snippets of the new code in the reply. The user reviews the changes
themselves in their own diff/editor, after the independent review in
step 6 has completed.

Infrastructure/ops concerns (server provisioning, MCP tooling, monitoring/
logging setup) are out of this loop entirely — not part of any request's
pipeline unless the user separately asks for ops work specifically.

## Tech baseline

- Unity **6.3 LTS** (`6000.3.23f1`), URP 17.3.0, Netcode for GameObjects
  2.13.2, Unity Transport 2.7.4, Multiplayer Play Mode 2.0.2 (needed for
  testing multiple clients in-editor — MPPM requires 2023.1+).
- Product name `encounter` (`ProjectSettings/ProjectSettings.asset`).
- **Git + LFS**: images/models/audio/fonts plus TerrainData `.asset`s go
  through LFS (`.gitattributes`). The five imported Asset Store packs
  (`Assets/PolysplitGames`, `Assets/Shinabro`, `Assets/Shinabro-combat`,
  `Assets/Spells Pack`, `Assets/TriForge Assets`) are **gitignored and
  stay local** — 3.9 GB.
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
done"). **It also can't catch anything gated behind `#if !UNITY_EDITOR`**
(e.g. `NetworkBootstrap`'s Standalone-only window-placement code) — it
references the Editor's own DLLs, so that code is never actually
compiled by it at all. A bug there only ever surfaces from a real
Player/Standalone build.

## Architecture

Scripts live in `Assets/Scripts/` (assembly `Encounter`, see
`Encounter.asmdef`); tests in `Assets/Tests/EditMode/` (`Encounter.Tests`).
Editor-only tools live in `Assets/Scripts/Editor/` under their own
`Encounter.Editor.asmdef` (`includePlatforms: Editor`) — **the folder being
named `Editor` is not enough**: Unity ignores that special name inside
another asmdef's folder, so without its own asmdef anything there compiles
into the runtime `Encounter` assembly and breaks player/server builds on
`using UnityEditor`. `Tools/CompileCheck.csproj` can't catch that (it
defines `UNITY_EDITOR` and compiles everything as one assembly). **asmdef
references aren't transitive for base-type resolution**: `Encounter.Editor`
and `Encounter.Tests` both reference `Encounter`, but referencing a type
that *extends* a type from a package (e.g. `CharacterWeaponVisual : 
NetworkBehaviour`) needs `Unity.Netcode.Runtime` listed directly in the
referencing asmdef too, not just inherited via the `Encounter` reference —
`Tools/CompileCheck.csproj` can't catch this either, for the same
one-assembly reason. Caught by the Editor's own compile, not by
`CompileCheck` — if the Editor reports a `CS0012` on a package type that
`Tools/CompileCheck.csproj` didn't, it's this.
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
    synced. Both sides share one start path each — the owner's
    `BeginPredictedCast`, the server's `RejectIfNotReady` (one cast at a
    time → global cooldown → own cooldown) + `StartServerCast` — for
    unit-targeted and ground-targeted casts alike. **Dying cancels a cast
    in flight**: `ResetCooldowns` (called from `PlayerRespawn.HandleDeath`)
    stops the pending resolve coroutine and clears `serverCastEndTime`
    along with the cooldowns, and the owner's cast bar with them —
    otherwise the respawned player is "Already casting" until the dead
    cast's timer runs out, and it resolves from the respawn point. The
    coroutine only clears `pendingCast` if `castSerial` still matches its
    own, since a new cast can be accepted in the same frame an old one
    finishes waiting.
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
    that currently sets it. No item currently grants this stat.
  - **Healing and shields**: `HitInfo` carries `Heal` and `ShieldAmount`
    alongside `Damage`, all handled in `ReceiveHit`.
    `CharacterStats.Heal(amount, healerClientId)` scales by the healer's
    `HealingMultiplier` stat and generates threat (see Enemy targeting
    below); aura-pulsed healing passes `NoAttacker` so it gets neither
    the multiplier nor the threat. `ShieldAmount`
    (`NetworkVariable<float>`) absorbs damage before health in
    `DealDamage`; a new grant replaces any remainder rather than
    stacking, and it has no duration cap — it persists until consumed.
    `ShieldVisual` (`Player.prefab` only, `Scripts/Combat/ShieldVisual.cs`)
    shows `shieldVfxPrefab` (Shield_Arcane) on the shielded character for
    as long as `ShieldAmount` stays above 0 — reacts to the
    already-everyone-synced `NetworkVariable` directly, so every client
    (not just the owner) sees it with no RPC needed; skipped on a
    headless dedicated server.
    `EffectOverheadVisual` (`Scripts/Player/EffectOverheadVisual.cs`) is
    the same everyone-sees-it pattern generalized to any status effect:
    shows a VFX above the character's head for as long as a specific
    `StatusEffectData` is in `CharacterStats.ActiveEffects` (also already
    synced to everyone, via `NetworkList.OnListChanged`) — currently
    wired once, for Vitality Ward (Blessing of Vitality's buff) with
    Buff_Light.
    `AuraGroundVisual` (`Scripts/Player/AuraGroundVisual.cs`) is the
    ground-level counterpart for the two movement-auras specifically:
    shows Aura_Arcane/Aura_Light at a player's feet for as long as
    `CharacterEquipment.HasReplenishmentAura`/`HasRegenerationAura`
    (two more everyone-synced flags, computed in `SetActiveAuras`
    alongside `CastAuraRevealsMobs`) is true. Both auras pulse on a
    repeating 6-tick cycle (tick = `AuraPulseInterval`, 1s) staggered by
    each player's rank among connected players (1-6, sorted by
    `OwnerClientId` — computed client-side by scanning `CharacterStats`
    the same way `PartyFrames.PartyNumber` does, since
    `NetworkManager.ConnectedClientsList` is server-only): each player's
    own 3-tick visible window (fade in, peak at the middle tick, fade
    back out — a half sine) starts on a different tick, so up to 6
    players' pulses don't all flash in sync. Implemented as a scale
    pulse (0 → full size → 0), not a true material alpha fade, since
    these are full imported multi-layer particle VFX and scale is what
    reliably fades "the whole effect" without reaching into every
    sub-emitter's own color curves.
    Both `AuraGroundVisual` and `EffectOverheadVisual` reposition an
    unparented VFX instance onto their character every frame, which only
    moves the *emitter*: these packs author their particle systems in
    World simulation space, where an already-emitted particle stays where
    it was born — so a long-lived one (an aura's ground ring) is simply
    left behind where the effect started. Both call
    `VfxScale.FollowInstance` right after instantiating, which switches
    every `ParticleSystem` on that *instance* (never the shared prefab)
    to Local simulation space so the particles ride along too.
    `CharacterWeaponVisual` (`Scripts/Player/CharacterWeaponVisual.cs`) is
    the same everyone-sees-it pattern applied to equipped weapons/shields:
    shows `ItemData.WeaponModelPrefab` in the wearer's equip socket
    (`CharacterAppearance.GetEquipSocket` - the Sidekick rig's own
    `prop_r`/`prop_l` joints, children of the hand joints, looked up by
    name and cached per rig root; falls back to the Humanoid hand bone
    for a rig without them) for as long as it's equipped in
    MainHand/OffHand, reacting to
    `CharacterEquipment.MainHandItemId`/`OffHandItemId` (two more
    `NetworkVariable<FixedString32Bytes>`, Server-written like
    `BroadcastsLocation` rather than owner-written like
    `CharacterAppearance`'s cosmetic Ids, since this must reflect what the
    server actually validated as equipped). Polls every `Update()` instead
    of only reacting to `OnValueChanged` — a still-unresolved socket (the
    rig not ready yet) just means "retry next frame", since the applied
    state is only recorded once the attach actually happens, with no
    explicit ordering needed against `CharacterAppearance`'s own
    `OnNetworkSpawn`. It rebuilds when the item Id *or the resolved
    socket* changes (`NeedsRebuild`, pure + tested) — an appearance
    rebuild replaces the whole rig, so the socket changes with it. A
    two-handed MainHand weapon leaves `OffHandItemId` empty
    (already true of `equippedItems[OffHand]`), so only one model ever
    shows, never a second one on the off hand.
    **Alignment is per weapon *category*, not per item**:
    `ItemData.AttachProfile` points at a shared `WeaponAttachProfile`
    asset (`Assets/Data/WeaponAttachProfiles/` — Sword/Staff/Axe2H/Bow/
    Mace/Shield/Wand; socket-local `Position`/`Rotation`/`Scale` plus an
    `AttachHand` override, since models from the same pack/type share a
    pivot convention). A new weapon of an existing
    category just points at that category's profile and needs no tuning;
    an outlier gets its own profile asset rather than a per-item override
    layer. The Bow profile is `Hand = Left` (the rig pack parents its own
    `BowRig` under `L_equip_joint`) even though a bow is a MainHand item.
    `CharacterWeaponVisual.ApplyAttachment` is the one place the
    socket-local transform is computed; its scale term cancels the
    skeleton's *internal* bone scaling (`rigRoot.lossyScale /
    socket.lossyScale`) so a model renders at its authored size with no
    fudge factor, while still following a scale applied to the whole
    character. Profiles are tuned visually, not typed:
    `Encounter → Weapon Attach Tuner` (`WeaponAttachTuner.cs`) builds a
    throwaway Sidekick character (the catalog's default presets, through
    `SidekickCharacterBuilder`) in edit mode, attaches the item's model
    through that same `ApplyAttachment`, and **Save To Profile** writes the
    gizmo-adjusted local transform back (its exact inverse); in the Editor
    the component also re-applies the profile every frame, so editing a
    profile asset during Play mode shows live and persists. Models +
    profiles are wired in bulk via `Encounter → Wire Weapon Models`
    (`WeaponModelWiringTool.cs`, same shape as `IconWiringTool.cs`). Not a
    NetworkObject — purely cosmetic,
    each client instantiates its own local copy, same approach
    `PlayCastVfxClientRpc` already uses for cast VFX, just persistent
    instead of timed. The Character Creation preview (`CharacterPreview`)
    does **not** show weapons (only the Equipment panel's preview does,
    via `CharacterPreview.RefreshWeapons`).
  - **Character appearance = Synty Sidekick presets** (`Scripts/Player/
    Sidekick/`): a player's look is a `SidekickSelection` — nine preset
    *names* (Head / Upper Body / Lower Body part presets, a Body Shape
    preset, and one color preset per Sidekick `ColorGroup`), exactly what
    Sidekick's own Presets tab offers, nothing finer-grained. Stored as
    `PlayerProfile.Sidekick*` strings, synced as one owner-written
    `NetworkVariable<FixedString512Bytes>` (`CharacterAppearance.Selection`,
    `;`-joined via `SidekickSelection.ToJoined/Parse`), and every peer
    builds its own local copy through `SidekickCharacterBuilder.Shared`
    (one `SidekickRuntime` + `DatabaseManager` per process) under the
    player's pre-placed `CharacterRoot` child. `SidekickPresetCatalog`
    lists what's offered: Head presets are Human + Unrestricted species
    only (the base character stays human), Upper/Lower Body presets come
    from **every** installed species (other packs' outfits on the human
    body — all parts share the `SK_BaseModel` skeleton), colors are
    Human + Unrestricted; `Resolve` swaps an empty/unknown part or body
    shape name for the group's first entry so a character can always be
    built, and drops an unknown color name to "" (base material).
    **Each build clones `M_BaseMaterial` and its six maps**
    (`SidekickRuntime.UpdateColor` paints preset colors into the
    material's textures with `SetPixel` — shared, every player would
    recolor every other player); `BuiltSidekickCharacter.Destroy` frees
    them with the character. **The server never builds** (`Apply` returns
    on `!IsClient`; the DB only exists as `Resources/Database/
    Side_Kick_Data.bytes` in a player build, written by the package's
    pre-build hook — run the Sidekick tool's *Sync Runtime Database* once
    and commit it): `CharacterRoot` carries a placeholder `Animator`
    (controller `CharacterIdle_M`, no avatar) so `NetworkAnimator.Animator`
    is non-null at `Awake` on every peer (its parameter caches are only
    built then — see the EnemyAI notes), and on the server that
    placeholder simply *is* `ActiveRigRoot`/`ActiveAnimator`. On clients a
    rebuild re-points `NetworkAnimator.Animator` at the built character's
    Animator. Animation needs no Sidekick-specific controller: the built
    character is Humanoid (`SK_BaseModel`'s avatar), so the existing
    `CharacterIdle_M` controller (Shinabro humanoid clips) retargets onto
    it unchanged. The old Polysplit-rig system (`CharacterAppearanceApplier`,
    `Appearance*` profile fields, `Resources/Data/Appearance*` assets,
    `CharacterRig_M/F` prefabs, `DrawLegacyAppearanceTabContent`) is still
    on disk but unreferenced. `Assets/Synty/SidekickCharacters/` is the
    one Synty pack committed in place (`.gitignore` negation) because the
    game loads it at runtime.
    `AbilityData.TargetVfxPrefab` is the target-side counterpart to
    `CastVfxPrefab` (which plays on the *caster*, for the cast-time
    window) — a purely cosmetic one-shot VFX played once at the
    *target*'s position when a direct-hit (non-projectile,
    non-ground-targeted) ability resolves successfully, via
    `PlayerAbilities.PlayTargetVfxClientRpc`, same broadcast-and-
    instantiate-locally approach as `CastVfxPrefab`/`Projectile
    .impactVfxPrefab`. Blessing of Vitality uses both: Casting_Light on
    the caster while casting, Spell_Light_6 on the target once it lands.
    **Resizing an ability's VFX**: `AbilityData.CastVfxScale`/
    `TargetVfxScale` (both default 1) scale the *instantiated* cast/target
    VFX only, never the shared source prefab — `VfxScale.Apply`
    (`Scripts/Combat/VfxScale.cs`) walks every `ParticleSystem` under the
    instance and multiplies each one's own local scale directly, since
    these imported VFX use Particle System Scaling Mode: Local
    (deliberately — some are also nested inside a differently-scaled
    parent elsewhere, e.g. `FireBolt.prefab`'s own 0.3-scaled root
    nesting the same `Projectile_Fire.prefab` Firebolt's cast VFX also
    uses directly, and Local mode is what lets that nested copy ignore
    the parent's scale and stay full-size — scaling only an instance's
    root transform does nothing under Local mode, and scaling the shared
    source prefab's own root/particle values directly would bleed into
    every other place that prefab is used). `EffectOverheadVisual
    .Mapping.Scale` (per entry, default 1 — 0 or less falls back to 1 for
    any entry serialized before this field existed) applies the same
    `VfxScale.Apply` to a status effect's persistent overhead VFX.
    `AbilityData.AreaAroundCaster` resolves centered on the caster's own
    position, hitting every `Targetable` (player or mob, caster
    included) within `GroundEffectRadius` — so a heal/shield AoE also
    heals/shields any mob standing in range, a deliberate side effect of
    "AoE hits anyone" applying to every ability, not just damage ones.
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
    `FollowingZonePrefab` on the target and calls `GroundPatch.Initialize`
    with `Effect` and two fields reused from the ground-patch system for
    their existing meaning — `GroundEffectRadius` (zone radius) and
    `PatchDuration` (how long the zone lasts) — plus the two optional
    arguments that make it a following zone rather than a fixed patch:
    `follow` (the target's `Transform`) and `excludedId` (the caster's
    `NetworkObjectId`). **There is no separate zone class** — a following
    zone and a fixed fire/ice patch are the same `GroundPatch`
    (`Scripts/Abilities/GroundPatch.cs`, `RequireComponent(SphereCollider)`)
    component: given a `follow` target it re-centers on it every
    `FixedUpdate` and scales uniformly (a dome) instead of horizontally
    only (a disc); given an `excludedId` it never affects that one
    character even if they stand inside it. Either way it drops any
    occupant that was destroyed while still inside (a mob that burned to
    death, a disconnected player — neither ever fires `OnTriggerExit`) and
    despawns itself once its own duration elapses, independent of what
    happens to the target. Arctic Winds uses this — its prefab still needs
    Editor setup, see "Not yet done".
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
    (permanent, equipment-less) — this is just a plain timed buff on whoever
    cast it. **Block chance**: `StatType.BlockChancePercent` — rolled in
    `CharacterStats.RollBlock` against any `HitSource.Melee` hit,
    zeroing the damage outright on success. Melee only, literally: a
    ranged weapon's basic attack (a player's bow, a Skeleton Archer's or
    Mage's shot) lands as `HitSource.Ranged` instead, via
    `WeaponData.BasicAttackSource` (`IsRanged` = `Range >
    BasicAttackRange` — the one shared definition of "ranged weapon", also
    what Arcane Shield and auto-attack line of sight key off), so arrows
    are never blockable. Equipment and effects both just
    add `Flat` `StatModifier`s to it like any other stat, so they stack
    additively through the normal `Stat` machinery (e.g. a 5%-from-gear
    shield plus a 25%-from-effect buff nets 30%) rather than one
    overriding the other. Aegis of the Ancient (`RequiresShield`,
    `SelfBuff`) grants +25% for its duration.
  - **Caster-facing AoEs**: `AbilityData.EnemiesAroundCaster`/
    `ConeAroundCaster` hit every other `Targetable` (player or mob,
    never the caster) within `GroundEffectRadius` (the cone variant also
    filtered by `ConeAngle` via `FacingCone.IsWithin`) — used by
    Reaper's Wheel/Seismic Slam (circle) and Cleave (cone). All melee
    AoE hits anyone, including other players, by design.
    `ChargeForwardDistance`/`ChargeToTarget` drive the caster via
    `PlayerMovement.ServerBeginPull`: the former (Trample) charges
    straight forward a fixed distance, hitting every other Targetable
    within 2.5 units of the line; the latter (Team Up) charges to
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
  - **Single-target-focus item bonuses**: `ItemData.SingleTargetBonuses` +
    `SingleTargetWindowSeconds` (default 12) — same once/sec evaluation
    cadence as HP-threshold bonuses (`CharacterEquipment
    .UpdateSingleTargetBonuses`, right after `UpdateHpThresholds` in
    `FixedUpdate`), active while the wearer has damaged or debuffed at
    most one distinct enemy within that rolling window, tracked via a new
    server-only per-player record: `CharacterStats.RecordEnemyInteraction`
    (called from `ReceiveHit` whenever a hit against a mob actually deals
    damage or applies a negative effect — not on every hostile interaction,
    unlike the broader `IsInCombat` tracker), `DistinctEnemiesTouchedWithin`
    (the query `CharacterEquipment` polls), and `RemoveTrackedEnemy`
    (called by `EnemyAI.HandleDeath` for every connected player, so killing
    your one tracked enemy lets you engage a new one without it counting as
    "now fighting 2" — the streak continues rather than resetting to 0).
    An AoE hit/debuff that touches 2+ enemies at once breaks the bonus on
    the next evaluation tick, same as any other once/sec-evaluated
    condition here. Modifiers are keyed by `(item, "singleTarget")`,
    distinct from `Bonuses`' plain-`item` source and `HpThresholdEffects`'
    `(item, index, bool)` tuples. Hunter's Cloak (+15% `DamageMultiplier`)
    uses this.
  - **Two-handed weapons**: `ItemData.TwoHanded` — a two-handed MainHand
    item occupies OffHand too. Enforced in `MainMenu`'s equip click
    handler (auto-clears the conflicting slot) and authoritatively in
    `CharacterEquipment.SetEquipmentServerRpc` (MainHand, slot 11, is always
    processed before OffHand, slot 12, so OffHand is forced null if
    MainHand resolved to a two-handed item).
  - **Dispel**: `AbilityData.RemovesNegativeEffect` strips one active
    `StatusEffectData.IsNegative` effect from the target (arbitrary pick
    if more than one is active) via `CharacterStats.RemoveOneNegativeEffect`.
    Every buff/aura effect defaults `IsNegative` to false and is never
    dispellable.
  - **Aura spells**: `AbilityData.IsAuraSpell` + `AuraRange` +
    `AuraReveals` — no cast, no keybind, no mana cost: simply having one
    slotted in the loadout (`PlayerAbilities.SetLoadoutServerRpc`) keeps
    it continuously active via `CharacterEquipment.SetActiveAuras`,
    which replaces its whole tracked set to match the current loadout
    every time it changes. Any number can be active at once (slot more
    than one), pulsed every `FixedUpdate` tick the same way an item's
    own `Auras` are (`PulseAura`) — including always passing
    `CharacterStats.NoAttacker` as the caster, same as item auras, which
    is what makes two different players both running the same aura
    spell collapse onto one shared effect instance per bystander rather
    than stacking (see Status effects below). A reveal-only aura is
    carried via `NetworkVariable<bool> CastAuraRevealsMobs` (true if
    *any* active aura ability reveals mobs), read by `PlayerHUD`
    alongside `ItemData.Reveals`. Echolocation is the reveal-only case
    (`AuraReveals`, no `Effect`); Aura: Replenish/Regeneration
    each pulse an `Effect` instead. The ability-slot key-press loop and
    `ResolveAbility` both explicitly skip `IsAuraSpell` abilities, since
    they're never cast through that path at all.
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
  skill slot Ids + hotkeys (`KeyCode` + shift flag), equipment Ids per
  `EquipmentSlot`, movement `KeyCode[]` indexed by `MovementAction`, UI scale.
  `ProfileStore.Current` loads it from
  `Application.persistentDataPath/profile.json` on first access and
  `Save()` is called when leaving a menu panel / closing the Escape menu
  / entering the testing area. `Normalize()` repairs array lengths so
  old files stay loadable, and clears any `SlotAbilityIds` entry whose
  Id no longer resolves in `GameDatabase` (a renamed/removed ability) —
  otherwise that slot would render as empty but still not accept a new
  ability, since "assign to the first empty slot" searches the raw
  string array, not what the UI displays. Local-only, not the account-backed
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
  is applied. `HitInfo.Source` (`Melee`/`Ranged`/`Ability`/`GroundPatch`/
  `Aura` — new values are always appended, never reordered) says where a
  hit came from. **Line of sight** is one shared rule,
  `CombatPhysics.HasLineOfSight` (`Scripts/Combat/CombatPhysics.cs`), used
  by every unit-targeted ability and by ranged auto-attacks (melee swings
  don't check it): only real environment geometry blocks — never a
  creature (anything under a `Targetable`), never a trigger volume (ground
  patches, following zones, pickups) — and it tests *every* collider along
  the line, not just the nearest, so a mob standing in front of a wall
  can't hide the wall. The right-click targeting raycast and the
  ground-aim raycast ignore triggers for the same reason.
  **Live registries, not scene scans**: `Registry<T>`
  (`Scripts/Data/Registry.cs`) is a self-maintained list of every enabled
  instance (`Add` in `OnEnable`, `Remove` in `OnDisable`), exposed as
  `Targetable.All` / `CharacterStats.All` / `EnemyAI.All` /
  `ThreatTable.All`. Nothing calls `FindObjectsByType` any more — anything
  needing "every mob", "every character", "every threat table" reads
  these. Code that deals hits while iterating (a hit can kill, a death can
  despawn) iterates `Registry<T>.Snapshot()` instead of the live list —
  one reused scratch copy per `T`, so two snapshot loops over the same
  type must never nest. **Effect immunities**: `ItemData.Immunities`
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
  **In-combat tracker** (players only): `CharacterStats.IsMob`
  (`GetComponent<EnemyAI>() != null`, cached in `Awake`) tells `ReceiveHit`
  which side of a hit is a mob; any hostile interaction between a player
  and a mob (either direction) calls `MarkInCombat()` on the player side,
  setting `IsInCombat` (a `NetworkVariable<bool>`) true and pushing a
  rolling 20s expiry out from `Time.time` — checked once per server
  `FixedUpdate` alongside the regen tick. Player-vs-player hits do
  nothing here (neither side is a mob). Doesn't re-fire on DoT ticks
  (those call `DealDamage` directly, bypassing `ReceiveHit`) — a
  mob-applied DoT only marks combat once, when first applied. Currently
  drives the weapon-pose idle's `showCombatIdle` gate (see Controls) —
  the intended pattern for anything else that should care about "is this
  player currently fighting a mob."
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
    `RefreshExtendOnly` rule. `EffectRejuvenation` is also what Aura of
    Regeneration pulses — since every aura pulse (item or spell) always
    passes the same `CharacterStats.NoAttacker` id regardless of which
    player is actually pulsing it, `StackPerCaster`'s per-caster key
    never differentiates between aura-casters in practice, so two
    different players both running that aura still collapse onto one
    shared instance for a bystander (no double-dipping) — it only
    genuinely splits per-caster for a real targeted cast like Everliving
    Touch, which passes each caster's own `OwnerClientId`.
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
  **Slows are the one deliberate exception to "different effects always
  stack independently"**: `StatusEffectData.IsSlow` marks any
  `RunSpeed`-reducing effect, and `CharacterStats.ApplyEffect` only ever
  lets the single strongest currently-active slow actually apply,
  regardless of which effect assets are involved or who applied them —
  a weaker slow landing while a stronger one is up is a complete no-op,
  a strictly stronger one outright removes every other active slow.
  Re-applying the *same* asset is untouched by this and still goes
  through its own normal `StackingMode` rule. Currently tags Icebolt's
  `slow`, Arctic Winds, Crippling Blow, and Tendon Shot.
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
- **Equipment**: `CharacterEquipment` re-syncs on `MainMenu.Closed`; only the
  first server-side application calls `RestoreFull()`, later swaps
  `ClampToMax()` (no free mid-fight heal). Wrong-slot items are rejected
  server-side. `CharacterStats.GetStat(StatType)` is the shared stat
  lookup.
  - **Server-wide item uniqueness**: at most one connected player may
    have a given item Id equipped at a time — `CharacterEquipment
    .globalItemOwners` (`static`, server-only, keyed by item Id) is
    claimed in `SetEquipmentServerRpc` and released whenever that item
    leaves a slot (`Equip`) or its owner disconnects
    (`OnNetworkDespawn`). Losing the race just silently drops that item
    from the requester's loadout, the same way an item in the wrong
    slot already does — there's no client-side awareness of who else
    holds what, so the Equipment menu can't warn you before you try, and
    if you lose the race your menu will keep showing it equipped locally
    (from `Profile`) until you reopen the Equipment page after the
    rejected sync. **Server-side statics start every session empty**:
    `globalItemOwners` and `ArcaneShieldZones` would otherwise survive
    stopping and re-hosting in the same process (or a whole Editor play
    session with domain reload off), leaving stale item claims and stale
    domes. `NetworkBootstrap.ResetServerSessionState` clears both on
    `NetworkManager.OnServerStopped` and once at startup
    (`RuntimeInitializeOnLoadMethod`) — deliberately **not** on
    `OnServerStarted`, because `StartHost` spawns the host's own player
    (which claims its equipment) *before* that event fires. Any new
    server-only static belongs in that reset too.
- **Enemy targeting**: `TargetingMode` (`Proximity`/`HighestThreat`/
  `LowestThreat`/`FarthestPlayer`) and the pure `TargetSelector.Select`
  live in `Scripts/Enemy/TargetSelector.cs`; `EnemyAI` just builds
  `TargetCandidate`s (threat, distance) from connected alive players.
  Whether a mob participates in threat is purely "does it have a
  `ThreatTable` component" (ogres yes, goblins no).
  - **Mob pathfinding**: `EnemyAI.ComputeChaseDirection` follows a
    `NavMesh.CalculatePath` result's corners instead of steering straight
    at the target, recalculating at most every `PathRecalcInterval`
    (0.25s, not user-specified — flagged in `review_with_fable.md`) via
    one reused `NavMeshPath` instance per mob, jittered per-instance on
    spawn so mobs summoned together don't all recalculate on the same
    physics tick forever after. Corners are read via
    `NavMeshPath.GetCornersNonAlloc` into a reused buffer array inside
    the recalc-gated branch only — `NavMeshPath.corners` itself is a
    property that allocates a new array on every single access, so
    reading it every tick (not just on recalc) would silently defeat the
    whole point of reusing one `NavMeshPath` instance.
    `Scripts/Enemy/MobPathing.cs` (pure, tested) picks the
    direction toward the path's second corner (the first is always the
    query's own source position) and falls back to the previous
    straight-line behavior whenever no usable path exists — no NavMesh
    baked yet (see "Not yet done"), the mob is off-mesh (e.g. airborne
    mid-knockback), or the destination is unreachable (a `PathPartial`
    result still walks toward the nearest reachable point, which needs no
    special-casing). No per-corner-index tracking across ticks between
    recalcs — accepted, bounded imprecision, same as any periodic-repath
    model. Only applied to "close distance to reach a target" chasing
    (the generic chase block and the Tactician's own closing-in branch) —
    deliberately **not** applied to `ServerBeginPull`/`IsPulling` (forced
    straight-line pulls — Vacuum-style abilities, Archer's
    `TriggerReposition` — by design) or kiting. **Agent type per mob**:
    `EnemyAI.ResolveNavAgentType` picks, once in `Awake`, the smallest baked
    NavMesh agent type (`NavMesh.GetSettingsByIndex`) whose radius and
    height contain the mob's *scaled* capsule (`NavAgentTypeSelector`,
    pure + tested) — so the 2×-scaled Ogre/Ogre Brute/Tactician use the
    big-agent surface while goblins/skeletons stay on Humanoid — and
    queries with a `NavMeshQueryFilter` carrying that id, starting from the
    capsule's feet (`FeetPosition`), not `transform.position`, which for a
    2× mob is 2 m above the mesh. No per-prefab setting: a new big mob
    just needs a surface baked for an agent type at least its size.
  - **Mob animation**: every mob visual gets its own dedicated
    `Assets/Animation/*Controller.controller`, never shared across mob
    variants (even ones that are otherwise identical, e.g. Goblin/
    Goblin Brute) — a shared controller means one mob's future retune
    silently affects every other mob pointed at the same asset, which
    is exactly the trap `MobSkeletonWarrior_1`/`MobOgre_1`/
    `MobOgre_Brute` were in before being split apart. Every one of these
    controllers has the identical shape: Idle/Run/Attack/Death states
    driven by a `speed` float (`EnemyAI`'s idle↔run blend) and
    `attack`/`death` triggers — `EnemyAI.Awake` wires whichever
    `Animator` it finds via `GetComponentInChildren<Animator>()` into
    the shared `NetworkAnimator` on the mob's root, and `FixedUpdate`
    drives `speed`/`attack`/`death` the same way for every mob
    regardless of which controller/clips it has. **That wiring has two
    hard requirements, both from how Netcode's `NetworkAnimator` is
    written**: its `Awake` only builds its parameter caches if its
    `Animator` is already non-null at that moment, and the server then
    calls `CheckParametersChanged()` every network tick with no null
    guard — so a `NetworkAnimator` that woke up first, or never gets an
    `Animator`, throws a `NullReferenceException` every tick. Hence
    `EnemyAI` carries `[DefaultExecutionOrder(-100)]` (its `Awake` must
    win the race rather than rely on Unity's unspecified component
    order), and it never leaves `NetworkAnimator.Animator` null — a mob
    with no `Animator` anywhere gets a bare placeholder one (a non-null
    `Animator` with no controller is handled safely by Netcode). `EnemyAI`'s
    *own* `animator` field, by contrast, is only set when the visual's
    `Animator` actually has a controller, so a controller-less mob skips
    every animation call instead of logging a warning per call. Mobs turn
    to face their target while attacking in range, not only while chasing.
    The clips themselves
    are generic Humanoid animations (the "Stander" rig, from
    `Assets/External/Shinabro`/`Shinabro-combat`) retargeted onto each
    creature's own Humanoid Avatar — not creature-specific animations —
    so the same four-state shape trivially extends to any new mob by
    just duplicating an existing controller and swapping which clips
    the four states point at. Currently wired: Ogre, Ogre Brute,
    Goblin, Goblin Brute, Skeleton Warrior (reuses the Ogre clip set),
    Skeleton Archer (uses the pack's bow-specific Idle/Attack clips
    instead of the generic sword&shield ones, since it's a ranged
    unit). **Skeleton Mage/Healer/Tactician have no Animator at all
    yet** — not just an unwired controller, the component itself is
    missing from their prefabs — a bigger gap than the others, not yet
    addressed.
  - **Healing threat**: `CharacterStats.Heal` also generates threat —
    15% of the amount actually healed (after `HealingMultiplier`), for
    both an instant heal and each individual HoT tick (both funnel
    through this one method, so every healing ability picks it up
    automatically). A heal never hits one specific mob, so there's no
    single `ThreatTable` to credit — `GenerateHealingThreat` walks
    `ThreatTable.All` and adds threat for the healer on every
    mob that **already has the healed character in its own table** (i.e.
    every mob currently fighting them). Gated on
    `healerClientId != NoAttacker`, which excludes aura-pulsed healing
    entirely (same reasoning `HealingMultiplier` uses), and on the healed
    character being a *player*: area heals hit mobs too, and a mob's
    `OwnerClientId` is the server's id — which on a host is also the host
    player's clientId — so healing a mob must not run that lookup at all.
    `Heal` itself is a no-op on a dead character (only `RestoreFull`
    brings one back), so a HoT tick or delayed heal landing in a mob's
    death-despawn window can't lift it back above 0 health.
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
  - **Skeleton Tactician escort** (see `BOSS_DESIGN.md` for the full
    encounter design): `EnemyAI.MobRole` (`None`/`SkeletonWarrior`/
    `SkeletonArcher`/`SkeletonHealer`/`SkeletonMage`/`SkeletonTactician`)
    — `None` (every existing mob) is completely unaffected by any of
    this. Each escort role's triggered abilities are checked every
    `FixedUpdate`, layered on top of (Warrior/Archer/Mage) or replacing
    (Healer holds position instead of chasing; Tactician gets its own
    movement state machine entirely) the normal chase-and-attack loop.
    Ally-awareness (HP-threshold heals/shields, nearest-Warrior
    repositioning, escort head-count) walks `EnemyAI.All` for matching
    `MobRole`s each check. The Tactician latches into melee for the rest
    of the fight once a player has attacked it (`HasBeenAttacked` — any
    `ThreatTable` entry above 0; healing threat can't create one, it only
    adds to an existing entry), gets within `tacticianMeleeEngageRange`,
    or fewer than 3 escort remain. **Tactical Instruction**: while an alive
    Tactician is within `tacticalInstructionRange` of an escort member,
    `UpdateTacticalInstruction` (state-change-gated, not re-applied
    every tick) grants that member a flat `ManaRegenRate` bonus and
    immunity to the Slow effect (`EffectImmunity`, same mechanism
    Ice Cleats uses); `FindTarget` separately swaps in priority
    targeting for Warrior/Archer/Mage while the aura's active, falling
    back to their own `targetingMode` otherwise. **Arcane Shield**:
    `ArcaneShieldZones` (`Scripts/Combat/ArcaneShieldZones.cs`) is a
    server-only static list of position+radius+expiry — no
    `NetworkObject`/physical presence at all — checked in
    `PlayerAutoAttack` and `PlayerAbilities.CastAbilityServerRpc`
    alongside the existing range/facing-cone checks, so a ranged
    weapon/spell simply can't target anyone standing inside one; melee
    is unaffected. Only enforced server-side (can't be predicted
    client-side), so it surfaces as a normal cast rejection. **Vision
    darkening** (Enshroud, Concussive Shot): `StatusEffectData
    .VisionRange` — while active, `PlayerCamera.UpdateVisionEffect`
    (owner-local) sets `RenderSettings.fog` to fade everything past
    that distance to black on the affected player's own screen only
    (`RenderSettings` is per-client, not networked, so nobody else's
    view changes). **Known simplification**: the Mage's "Icebolt" is a
    plain `WeaponData` (same damage + Slow effect as the player spell)
    fired through the ordinary basic-attack path, not an actual cast —
    it does the same damage and applies the same slow, but doesn't
    spawn ice ground patches the way the player version does.
- **Menus / dev UI** (`Scripts/UI/`, one remaining IMGUI `OnGUI` panel —
  deliberately disposable, don't invest further in it; real UI is UI
  Toolkit, see the migration paragraph below): pregame `MainMenu` (Skills /
  Equipment / Options / Enter Testing Area) and, after
  `TestingAreaGate.Entered`, the same panels as an **Escape menu**
  (`MainMenu.IsOpen`). The Equipment page is a paper-doll: 3×5 grid of
  equipment slots on the left, inventory grid (every unequipped item in the
  game, no real inventory yet) on the right - see the UI Toolkit migration
  paragraph below for `EquipmentPanelController`'s specifics. While open,
  `PlayerMovement`, `PlayerCamera`, `PlayerTargeting`, `PlayerAbilities`
  ignore gameplay input; on close `MainMenu.Closed` triggers
  loadout/gear re-sync. Options page: UI scale and look sensitivity are
  both sliders (75–250% / 0.25×–3×, `UIScale`/`LookSensitivityScale` —
  same profile-backed pattern), plus movement rebinding (any non-mouse,
  non-Escape key; binding a key steals it from other movement actions
  *and* ability slots, and vice versa). Look sensitivity is a single
  multiplier applied on top of `PlayerCamera`'s pitch/free-look yaw and
  `PlayerMovement`'s turn yaw, not three separate sliders.
  Ability hotkeys accept any non-mouse, non-Escape key, with an optional
  Shift modifier (`MainMenu.CaptureAbilityKey` captures whichever key was
  pressed plus whether Shift was held at that moment, rather than
  matching against a fixed list) — Escape is the one key that can never
  be bound to anything, ability or movement (explicitly excluded in both
  capture loops, on top of `Update()` already intercepting and consuming
  it before either loop runs). `MovementAction` also covers what used to
  be fixed/hardcoded controls — `CycleTarget` (Tab), `SelfTarget`
  (backtick), `PartyTarget1`-`5` (F1–F5), `ZoomIn`/`ZoomOut` (Up/Down
  arrow) — all rebindable the same way movement is, defaulting to their
  old keys. New `MovementAction` values are always appended, never
  inserted/reordered, since `MovementInput.KeyFor` indexes
  `PlayerProfile.MovementKeys[]` positionally by `(int)action` —
  reordering would shift every existing saved profile's bindings onto
  the wrong action.
  `DevGui.Begin()` (UI scale) must be the first line of every `OnGUI`,
  laying out against `UIScale.Width/Height`. The Escape menu also has a
  **Summon Mobs** page (`PlayerSummon` on the local player object does
  the spawning; the menu just drives it). **No text fields in in-game
  panels**: IMGUI's native Tab focus traversal moves keyboard focus into
  any focusable control even when the Tab event is `Use()`d, and Tab is
  the tab-targeting key — the summon count is −/+ buttons for exactly
  that reason. The only text field is the server-address box, which is
  gone once connected.
  **UI Toolkit migration** (in progress, panel by panel): `Assets/UI/` holds
  `Theme.uss` (shared design tokens as USS custom properties, scoped under a
  `.theme-root` class since USS has no universal `:root`, plus reusable BEM
  component classes — `.panel`, `.button`, `.field-row`, etc.) and each
  panel's own `<Panel>.uxml`/`<Panel>.uss`. Migrated so far: Options
  (`OptionsPanelController`), the main/in-game menu shell
  (`MenuShellController` — one UXML for both the pregame button set and the
  in-game one, toggled via `SetMode(bool inGame)` rather than two separate
  panels), Summon Mobs (`SummonPanelController`), and Skills
  (`SkillsPanelController` — "Your Kit" renders as a horizontal toolbar
  mirroring the real in-game ability bar's shape rather than a vertical
  list; "Available Abilities" is a tab bar, one tab per fixed category
  (Auras/Heals/Melee/Damage Spells/Utility, computed by
  `MainMenu.CategorizeAbility` purely from fields already on `AbilityData`,
  checked in that priority order, first match wins — all 5 tabs always
  exist even when a category is currently empty), each ability shown as a
  card: name + Mana Cost on one row (Mana omitted entirely for aura spells,
  which have none), then `TooltipText.BuildAbilityBody`'s description below
  — a data-driven stat/effect breakdown (no authored flavor text exists on
  `AbilityData`) formatted in the standard MMO spell-tooltip convention
  (WoW's, specifically): a compact cast-time/cooldown/range block first,
  then the effect as a plain-language sentence ("Deals 40 damage.") rather
  than bare "Label: value" lines. The tab row itself is `CategoryTabBar`
  (`Scripts/UI/CategoryTabBar.cs`, a plain C# helper owned by the
  controller, like `HoverTooltip`) - shared with the Equipment panel; which
  tab is selected is view state it owns, not threaded through `MainMenu`,
  since it has no bearing on `Profile`/game data. Each kit slot's icon shows a `HoverTooltip` with the
  same name/mana-or-passive/body content as its Available Abilities card
  (`SkillsPanelController.BuildKitSlotTooltipText`). Kit slots are also
  drag-to-reorder: a filled slot's icon handles `PointerDown`/`Move`/`Up`
  itself (not a `Button` - Unity's built-in `Clickable` manipulator can't
  be reliably suppressed once a drag has started) to distinguish a plain
  click (open the sub-row, via `SlotClicked`) from a drag past
  `DragThreshold` (raises `SlotReordered(from, to)`, handled by
  `MainMenu.ReorderSkillSlots` → `PlayerProfile.SwapSlots` - swaps
  ability/key/shift-flag together, works the same whether the drop target
  is empty or occupied). `PointerUpEvent`'s handler reads `isDragging`
  and computes the drop target *before* calling `ReleasePointer` -
  releasing capture can synchronously fire `PointerCaptureOutEvent`,
  whose handler resets that same drag state, so reading it afterward is
  unsafe.), and Equipment (`EquipmentPanelController` — the same
  paper-doll-left/inventory-grid-right layout and click-based interaction
  the old `DrawGearPanel` had: click an inventory item to equip it into
  `TargetSlotFor`'s resolved slot, click a filled slot to unequip it, same
  two-handed-weapon/off-hand conflict auto-clearing. `ItemData.Icon` now
  actually renders here (wired via `Encounter/Wire Icons`) instead of the
  old "X" placeholder; a null `Icon` just shows an empty icon box, same
  as an ability without one in Skills. Per-slot-category outline colors
  (rings red, trinket green, main hand cyan, off hand purple, necklace
  yellow, chest blue, boots black) are unchanged, just drawn as a
  `VisualElement` border instead of `GUI.DrawTexture`. The inventory side
  (not the equip grid) is a tab bar, one tab per equipment slot category
  (Head/Neck/Chest/Cape/Gloves/Legs/Boots/Rings/Trinket/Main/Off - Ring1
  and Ring2 collapse into one "Rings" tab since a ring item's own `Slot`
  is always just the `Ring1` category), same
  always-show-every-tab-even-empty convention and the same shared
  `CategoryTabBar` Skills uses; `MainMenu
  .InventoryCategorySlots`/`InventoryCategoryNames` bucket
  `GameDatabase.Items` into each tab by `item.Slot`. `.category-tabs`/
  `.category-tab`/`.category-tab--selected` live in `Theme.uss`, shared
  between Skills and Equipment rather than duplicated per panel. Drag-and-
  drop equip is a deliberate future follow-up, not this pass.) —
  `Appearance` is still IMGUI (`DrawAppearancePanel` — one `DrawSelectableList`
  of Sidekick preset names per tab, see Character appearance above). Each `*Controller`
  (`Scripts/UI/*Controller.cs`, on its own child GameObject under `MainMenu`,
  each driving its own sibling `UIDocument`, all sharing the same
  `EncounterPanelSettings.asset` — **`MainMenu` itself must never carry a
  `UIDocument` component**: Unity auto-nests a child GameObject's
  `UIDocument` content inside a *parent* GameObject's `UIDocument` tree if
  the parent has one too, which silently breaks the child's layout (renders
  at an unresolvable/NaN size — invisible, no console error) — every
  panel's `UIDocument` must live on its own child GameObject, siblings to
  each other, with `MainMenu` staying a plain GameObject holding only the
  `MainMenu` script) is purely presentational — it never
  touches `ProfileStore`/game state itself, only displays what `MainMenu.cs`
  pushes into it (each frame the panel is open, for anything with per-frame-
  changing content) and raises plain C# events that `MainMenu.cs` handles,
  so all real state/logic stays in the one place it always has been. Every
  controller resolves its `UIDocument.rootVisualElement`/child elements
  lazily on first use (`EnsureInitialized()`), not from `Awake()` — a
  `UIDocument` builds its root in its own `OnEnable`, which runs after every
  object's `Awake` in the scene, so touching it any earlier risks a null
  root; `MainMenu.Start()` (never `Awake()`) is where any one-time
  list-building against a controller happens for the same reason.
  `MainMenu.RefreshPanelVisibility()` centralizes which one of
  {shell, Options, Summon, …} is currently visible, based on `activePanel`/
  `IsOpen`/`TestingAreaGate.Entered`, called from every panel-transition
  method (`OpenPanel`/`LeavePanel`/`OpenInGameMenu`/`CloseInGameMenu`/
  `Start`) — adding a future migrated panel only needs one more line there,
  not changes to every transition method. `UIScale.Value` (the existing
  player-facing slider) applies to every UI Toolkit panel via
  `VisualElement.style.scale`, applied on `Show()`, instead of IMGUI's
  `GUI.matrix` — same profile-backed value either way, so sizing stays
  consistent while some panels are still IMGUI and some aren't.
  **Hover tooltips**: `HoverTooltip` (`Scripts/UI/HoverTooltip.cs`, plain C#,
  not a `MonoBehaviour`) is the UI Toolkit replacement for IMGUI's automatic
  `GUI.tooltip` hover-tracking — a panel that needs hoverable rows (Skills,
  Equipment) constructs one and calls `Attach(element, () =>
  tooltipText)` per hoverable element; it's a cursor-following floating box,
  content fetched fresh on every hover. `TooltipText`
  (`Scripts/UI/TooltipText.cs`) holds the actual pure ability/item tooltip
  text formatting, shared by both.
  **Gotcha**: `HoverTooltip`'s floating element is added directly to
  `UIDocument.rootVisualElement`, which is a *different* element from the
  UXML's own named top-level element that actually carries the
  `theme-root` class (that one is a child of `rootVisualElement`, not
  `rootVisualElement` itself) — every controller that constructs a
  `HoverTooltip` must call `root.AddToClassList("theme-root")` on
  `rootVisualElement` first, or every `var(--color-*)` on the tooltip
  silently falls back to UI Toolkit's defaults (black text, no themed
  background/border) instead of erroring.
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
  - **Party targeting** (`MovementAction.PartyTarget1`-`5`, default
    F1–F5, rebindable): `PlayerTargeting` targets whoever is drawn in
    that *row* on the viewer's own screen (`PartyTarget2` = second row),
    via `PartyFrames.GetDisplayOrder`'s returned order — **not** the
    same as `PartyNumber`. Row index is viewer-relative (depends on
    which entry got skipped for being "you"); `PartyNumber` is the same
    for everyone regardless of who's watching. Example: canonical order
    P1,P2,P3,P4 — P2's screen shows rows [P1, P3, P4] labeled "Player
    1"/"Player 3"/"Player 4"; P2's `PartyTarget2` hits row 1 → P3, even
    though P3's own label says "Player 3", not "Player 2".
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
  of their own reveals (**Transmitting Beacon**, Ring1). **Equipment
  slots: 12** (`EquipmentSlot` — no Belt, and `Ring1`/`Ring2` only, no
  Ring3/4). Rings are **interchangeable**: a ring item's `Slot` is just
  the `Ring1` category, `EquipmentSlotExtensions.IsRing()` treats either
  physical slot as valid for it in `CharacterEquipment
  .SetEquipmentServerRpc`'s placement check, and `MainMenu.TargetSlotFor`
  picks whichever physical ring slot is free (Ring1 first) when equipping
  one from the inventory grid. `EquipmentSlot.Legs` is pinned to its old
  underlying value (`= 6`)
  so removing Belt didn't shift every later slot's serialized value out
  from under existing item assets. No terrain by design. Disc/blip
  textures are generated at runtime.
- **Controls**: W/S forward/back (both mouse buttons also = forward), A/D
  strafe, Space jump, `\` auto-run (cancelled by W/S or opening the
  menu), right-drag turns the body, left-drag free-looks the camera.
  The character's visual model also faces the direction of net WASD
  movement, but only while forward input is forward or neutral — straight
  ahead for plain forward, 90° for a pure strafe, a blended diagonal for
  forward+strafe (`PlayerMovement.FixedUpdate` sets
  `CharacterAppearance.ActiveRigRoot`'s local yaw every tick via
  `Atan2(strafeInput, forwardInput)` — level-triggered from current input,
  not a one-shot turn-per-press, so it can't accumulate and always
  resolves back to neutral on its own). Any backward component (S alone,
  or S+A/S+D) instead always faces forward, ignoring strafe entirely for
  this calculation — actual movement itself is unaffected either way,
  this only governs which way the model visibly faces while backpedaling
  in any combination. The active rig only, never the player's own
  transform, which also drives movement direction, the camera, and
  `FacingCone` combat checks — purely cosmetic by design: it doesn't
  affect movement direction, the camera, or combat facing/hit detection,
  and isn't currently visible to other clients (same limitation as the
  still-open cross-client animation sync issue).
  **Weapon-type idle pose**: `WeaponData.PoseType` (`WeaponPoseType`:
  Unarmed/OneHand/TwoHand/Staff/Bow/DualWield — set per weapon asset, e.g.
  Broad Sword is OneHand, 2H Axe is TwoHand) classifies what a weapon
  calls for; `PlayerMovement.ResolveWeaponPoseParameter` (owner-local,
  same `ProfileStore.Current.GetEquipment` pattern `HasHoverBoots` uses)
  combines the equipped MainHand weapon's `PoseType` with an OffHand
  shield check (a `OneHand` weapon + a shield item resolves to a distinct
  `OneHandShield` pose) to drive a `weaponPose` int Animator parameter
  every tick, right alongside `speed`. Unlike the movement-facing turn
  above, this rides the same owner-authority `NetworkAnimator` `speed`
  already uses, so it replicates to other clients automatically. No
  off-hand weapon items exist yet, so `DualWield`
  isn't reachable with real gear currently, only architecturally
  supported for whenever one exists. The weapon-pose idle only shows
  while targeting an NPC or `CharacterStats.IsInCombat` is true (see
  Combat pipeline below); a `showCombatIdle` bool Animator parameter
  (`PlayerMovement.ResolveShowCombatIdle`) carries this alongside
  `weaponPose`, and the Animator Controller falls back to a relaxed
  `Idle_OutOfCombat` pose (`Stander@Sub_Idle2`) otherwise. There's no
  dedicated Unarmed idle state at all — a bare-fists player (no MainHand
  weapon) always shows `Idle_OutOfCombat` too, even while targeting or
  fighting a mob (`ResolveShowCombatIdle` special-cases `weaponPose == 0`
  to always return false). The Animator-side state names are
  `Idle_1hWeapon`/`Idle_1hWeaponShield`/`Idle_2hWeapon`/`Idle_Staff`/
  `Idle_Bow`/`Idle_DualWield`/`Idle_OutOfCombat`.
  Tab (`MovementAction.CycleTarget`) cycles targets, `` ` ``
  (`SelfTarget`) self-targets — both rebindable, same as party targeting
  above — Escape (the one permanently fixed, never-rebindable key)
  clears the target first and opens the menu only when nothing is
  targeted. **Left-click**: a click (not a drag) on a creature/player
  targets them, same click-vs-drag distinction right-click already uses
  (`PlayerTargeting.IsClick`, shared thresholds) — holding and dragging is
  still free-look (`PlayerCamera`), unaffected. Unlike right-click, a
  left-click only targets — it doesn't arm auto-attack, and clicking empty
  space does nothing (doesn't clear the target either, same as
  right-click). Up/Down arrow (`ZoomIn`/`ZoomOut`, rebindable, held)
  zooms the camera in/out along its own local Z offset from
  `CameraPivot`, clamped between `CameraZoomScale.Min`/`Max` — saved to
  the profile (same set-in-memory-then-an-existing-Save()-trigger-
  persists-it pattern as UI Scale/Look Sensitivity), so it restores on
  respawn/rejoin.
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
`Assets/Materials`, `Assets/Animation`, `Assets/UI` (UI Toolkit `.uxml`/`.uss`,
see Menus/dev UI above), `Assets/Resources/Data/*`,
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
- **Primary monitor on launch**: `NetworkBootstrap.Start` calls
  `Screen.MoveMainWindowTo(Display.displays[0], ...)` in every
  Standalone build (skipped in the Editor, and in batch mode since a
  dedicated server has no window) — otherwise a Windows build reopens
  on whichever monitor it last used, which Unity caches per-machine.
- A Windows Standalone build was sent to the user's brother and works
  end-to-end against the VPS.

## Not yet done

1. systemd unit for the dedicated server (still just a manually-launched
   `nohup` process — no auto-restart on boot/crash). A fresh Linux
   server build was shipped to the VPS mid-session; it's since fallen
   behind again (the Skeleton Tactician work and everything after it),
   so another redeploy is needed once that's ready to test live.
2. Fire/ice ground patches as URP Decal Projectors (visual only; needs
   the Decal renderer feature added to the three URP renderer assets in
   the Editor first) and real particle VFX instead of coloured discs.
3. The designed systems in `DESIGN_IDEAS.md` / `ARCHITECTURE_NOTES.md`:
   loot, account-backed unlock-gated profile, encounter wipe/reset state
   machine, taunt/threat reset on combat end.
4. Consider downsizing the largest TriForge textures in `Assets/External`
   (several 50–100 MB 4K PNGs) — LFS is ~1.1 GB, near GitHub's free tier.
5. **Mob pathfinding via NavMesh — Editor setup done, partially working.**
   `EnemyAI.ComputeChaseDirection` (see Enemy targeting below) follows a
   NavMesh path when chasing. `com.unity.ai.navigation` is installed, a
   `NavMeshSurface` is on the Terrain (`CollectObjects: All`) and baked,
   and the scene has been saved with it. Confirmed working: mobs path
   around trees correctly. **Still an open issue**: mobs have a hard
   time pathing around the TriForge ruins props and "other objects" —
   symptom not yet fully diagnosed (last checked: need to confirm whether
   the baked NavMesh actually has a hole around a ruin at all, and
   whether the mob walks straight through it vs. gets stuck vs. takes an
   odd detour, to tell a bake/geometry-collection gap apart from a
   pathing/connectivity issue like a too-narrow gap). Rebake after any
   terrain/prop layout changes. **Leading suspect, now addressed but not
   yet confirmed in play**: the 2×-scaled mobs were pathing on the
   Humanoid-sized (r 0.5 / h 2) mesh while physically being r 1 / h 4,
   and querying from a point 2 m above it — a second NavMeshSurface for a
   larger agent type was baked and `EnemyAI` now picks it per mob (see
   Mob pathfinding above). Retest ogres around the ruins before closing
   this out.
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
   create a GameObject (a default Sphere primitive if you want the dome
   visible, or empty for an invisible trigger), (2) add a `NetworkObject`
   component, (3) add `GroundPatch` (`Scripts/Abilities/GroundPatch.cs` —
   the same component the fire/ice patches use; its
   `RequireComponent(SphereCollider)` adds the collider automatically, and
   `Initialize` scales the whole object so a default 1-unit sphere ends up
   exactly the zone's radius, visual and trigger together), (4) save
   as a prefab, (5) register it in `DefaultNetworkPrefabs.asset`, (6)
   drag it onto `AbilityArcticWinds`'s `FollowingZonePrefab` field. Like
   the fixed patches, it has no `NetworkTransform` of its own — if you
   want *clients* to see a visible dome track the target (rather than
   only the server-side trigger following it), add one.
8. **Mana orb pickup prefab — built, just needs network registration.**
   `Assets/Resources/Prefabs/ManaOrb.prefab` exists now, but it's not yet
   registered in `DefaultNetworkPrefabs.asset` like every other spawned
   prefab — without that, `ManaOrb.TrySpawn`'s `NetworkObject.Spawn()`
   call likely won't replicate correctly to other clients. Also:
   `Assets/Resources/Prefabs/HealthOrb.prefab` exists but nothing in the
   scripts references "HealthOrb" at all — no drop logic spawns it, it's
   currently an orphaned prefab.
9. **Skeleton Tactician escort — visuals and network-prefab registration**
   (see `BOSS_DESIGN.md`). The five `MobSkeleton*.prefab` files exist
   with all their stats/weapon/effect references already wired, but
   still need: (1) each one's actual visual model parented under its
   root the same way `VisualGoblin_1` is parented under `MobGoblin_1`
   (user is attaching `Skeleton_Warrior`/`Skeleton_Archer`/
   `Skeleton_Mage` etc. from `Assets/PolysplitGames/
   LowPolyMedievalFantasyBipedCreatures/` separately from this code
   work) — the Healer and Tactician don't have an obvious matching
   model in that pack, may need a reused/re-skinned one; (2) all five
   registered in `DefaultNetworkPrefabs.asset` like every other spawned
   prefab, or they can't be spawned at all yet; (3) if you want them
   test-summonable, add them to `PlayerSummon`'s `summonableMobs` on
   `Player.prefab`. **Arcane Shield's cooldown (`arcaneShieldCooldown`
   on the Mage's `EnemyAI`) is a flagged placeholder (30s)** — never
   specified in the design, see `review_with_fable.md`.
10. **Only weapons currently render a visual model on equip** (via
    `CharacterWeaponVisual`/`WeaponAttachProfile`) — no other equipment slot
    has a visual representation yet. Hunter's Cloak (Cape) has no 3D asset
    and isn't wired; this is the current scope, not a gap specific to that
    item.
11. **Sidekick appearance — Editor wiring still pending** (the code side
    is done, see Character appearance above): (1) `Player.prefab` needs an
    empty child `CharacterRoot` (local identity) with an `Animator`
    (Controller `CharacterIdle_M`, Avatar none), assigned to
    `CharacterAppearance.Character Root` (+ `Animator Controller` =
    `CharacterIdle_M`) and to `NetworkAnimator.Animator`; the old
    `CharacterRig_M/F` instances removed or disabled. The placeholder's
    controller must be the same `CharacterIdle_M` the built character
    gets: `NetworkAnimator` sizes its parameter/layer caches from its
    Animator once at `Awake`, and the later re-point is a bare field
    write. (2) The scene's `CharacterPreviewStage` likewise needs a
    `CharacterRoot` child assigned to `CharacterPreview.Stage Root` (+
    `Animator Controller` = `CharacterIdle_M`), the old rig instances
    removed, and the preview camera reframed for the Sidekick height.
    (3) Run the
    Sidekick tool's *Sync Runtime Database* once so
    `Assets/Synty/SidekickCharacters/Resources/Database/Side_Kick_Data.bytes`
    exists (player builds read only that), and commit it. (4) Re-tune the
    seven `WeaponAttachProfile`s with the Weapon Attach Tuner — the
    sockets are now `prop_r`/`prop_l` with a different pivot. (5) Redeploy
    the dedicated server afterwards.

## Notes for future sessions

Treat the above as established direction. `DESIGN_IDEAS.md` is game
design, `ARCHITECTURE_NOTES.md` is how the code must be shaped to serve
it, `BOSS_DESIGN.md` is encounter design.
