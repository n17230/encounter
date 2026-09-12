# reallyfungame — Architecture Notes

Living document for code-architecture considerations driven by design
decisions in `DESIGN_IDEAS.md`. Separate from `CLAUDE.md` (project/tech
setup) and `DESIGN_IDEAS.md` (game design) — this file is about *how the
code needs to be structured* to support the design, not the design itself.

## Gear & ability system — modularity requirement

- Players pick and choose their own gear and abilities (see
  `DESIGN_IDEAS.md` — items & progression, pregame menu). Since any
  character can use any item/ability with no restrictions, and loadouts
  are player-chosen rather than fixed per-class, the gear/ability system
  needs to be **modular and data-driven** rather than hardcoded per
  character.
- Implication: adding a new item or ability should not require touching
  character code — items/abilities should be defined as data instances
  of a common base type, not one-off classes.
- Need to define **base-level characteristics** shared by all gear and
  all abilities (stats/effects they can carry, how they hook into
  character logic) before building specific items — this is the next
  design conversation to have.
- Unity-specific option to consider (not yet decided): ScriptableObjects
  are a natural fit for data-driven item/ability definitions, since they
  let each item/ability be authored as a data asset rather than code.
- **Fixed stats per item** (see `DESIGN_IDEAS.md` — no random rolls or
  rarity variance in v1) — simplifies this further: an item's data asset
  IS its final stat block, no roll-generation logic needed on
  acquisition/drop.
- **Gear can grant/modify abilities** (see `DESIGN_IDEAS.md`) — gear and
  abilities are not fully independent systems. An item's data needs to be
  able to reference an ability to grant, or a modification to apply to an
  existing one — the gear system and ability system need a defined
  integration point, not just separate parallel data models.
- **Mixed ability targeting requirements** (see `DESIGN_IDEAS.md`
  targeting section) — an ability's base data needs a
  targeting-requirement field (needs a target / no target — self-cast
  or ground-targeted) rather than assuming every ability needs a
  selected target. No hostile/friendly validation needed on top of that
  — any ability can be cast on any target type.
- **Range, line-of-sight, and cooldown are universal ability
  requirements** (see `DESIGN_IDEAS.md`) — every ability's base data
  needs a range value and a cooldown value, and casting needs a
  server-side LoS check against the target, regardless of ability type.
  These are core fields on the shared ability base type, not
  per-ability special cases.
- **Cast time and mana cost are per-ability fields, not constants** (see
  `DESIGN_IDEAS.md`) — the ability base type needs a cast-time field
  (zero for instant abilities) and a mana-cost field, each set per
  ability rather than assumed uniform. A nonzero cast time implies a
  server-side "casting" state (interruptible in principle) between
  activation and effect resolution, distinct from an instant ability's
  immediate resolution.

## Account & character model

- **One character per account** (see `DESIGN_IDEAS.md`) — no
  character-select flow or per-character save slots needed; account data
  and character data can live in a single record rather than
  account → list-of-characters.

## Character base stats

- Every character (all base stats a common `Character` type needs to
  carry, before any gear/ability modifiers are applied):
  - Health
  - Mana
  - Run speed
  - Armor
- Gear, abilities, and boss effects (e.g. Boss 1's DPS-taken → speed
  scaling in `BOSS_DESIGN.md`) will need to **modify these at runtime**,
  not just read them — implies a stat system with base value + active
  modifiers (buffs/debuffs/gear bonuses) rather than flat fields, e.g. a
  `Stat` type wrapping base value + a list of modifiers, rather than a
  plain `float health`.
- This stat system is what the gear/ability base-characteristics model
  (above) will plug into — an item/ability's "effect" is likely expressed
  as a modifier against one or more of these stats.
- **Armor = a single % damage reduction stat** (see `DESIGN_IDEAS.md`) —
  no physical/magic damage-type split, so the damage pipeline is simple:
  one mitigation percentage applied to any incoming damage, regardless
  of source. No need for a damage-type enum or per-type resistance
  stats.
- **Mana is the only resource** (see `DESIGN_IDEAS.md`) — no need for a
  generic "resource type" abstraction (energy/rage/etc.); abilities can
  just reference the single mana stat directly for cost.
- **Health and mana both regenerate over time** (see `DESIGN_IDEAS.md`)
  — the `Stat` type (or the `Character` holding it) needs a per-tick
  regen behavior for these two, not just a static current/max value pair.
- **Gear can boost max health, max mana, and health/mana regen rate**
  (see `DESIGN_IDEAS.md`), on top of armor — implies **regen rate itself
  needs to be a modifiable `Stat`**, not a hardcoded constant, since gear
  needs to add to it the same way it adds to max health/mana. So the
  character's stat set is effectively: health, max health, health regen
  rate, mana, max mana, mana regen rate, run speed, armor — each
  independently gear/ability-modifiable.
- **Run speed is a flat value** (see `DESIGN_IDEAS.md`), not a
  base-times-multiplier system. It still fits the same modifier-based
  `Stat` model above — Boss 1's DPS-taken speed scaling (see
  `BOSS_DESIGN.md`) is just another modifier applied to that flat value
  at runtime, not a separate multiplier layer.

## World & session architecture

- World structure is **hybrid**: persistent shared overworld (all
  connected players in one continuous world state) + **instanced**
  sessions for boss/dungeon content (see `DESIGN_IDEAS.md`).
- The **6-player cap is server-wide** (not per-instance) — meaningfully
  simplifies this: there is at most one group's worth of players on the
  server at any time, so instancing doesn't need to handle multiple
  concurrent independent groups. Likely reduces to a single overworld
  scene + a scene-swap (or additive scene load) into instance content for
  whichever players enter it, rather than a multi-tenant instance server.
- Still need: a way to move players from overworld into an instance and
  back, and to sync loot/results back to persistent accounts afterward.
- Under Netcode for GameObjects (see `CLAUDE.md`), scene-based separation
  on a single server process now looks like the natural fit given the
  hard 6-player cap — worth confirming once past the movement prototype,
  but a multi-process instance server is probably unnecessary scope.
- The **overworld is a small social lobby/hub**, not explorable content
  (see `DESIGN_IDEAS.md`) — further reduces scope for the persistent
  scene: no streaming/procedural terrain concerns, likely just a single
  small hand-built scene.
- **Portal-based transition**: players enter an instance via a portal
  object in the overworld, not a menu/matchmaking flow — implies a
  server-side interactable that, on group interaction, moves the
  relevant players' netcode connections/objects from the overworld scene
  into an instance scene (and presumably back out through a similar
  mechanism, or on instance completion/wipe-exit).

## Class / role system — no class data model needed

- Freeform, no classes (see `DESIGN_IDEAS.md`) — there is **no class
  entity/type to model**. A player's role is just an emergent property of
  their chosen loadout (which gear/abilities they brought), not something
  the code needs to track or gate.
- Simplifies the gear/ability modularity system above — items/abilities
  only need to check account-level unlock status, never a class
  requirement.

## Encounter / wipe state

- Instanced boss encounters use a **wipe/reset on failure** model (see
  `DESIGN_IDEAS.md` — Death & failure states), not per-character
  respawn-and-continue.
- Implies each instance needs an **encounter state machine** (e.g.
  in-progress → wiped → reset) that can restore the group and the boss to
  a pre-pull state — distinct and separate from overworld death handling,
  which is just a plain respawn (see `DESIGN_IDEAS.md` — Death & failure
  states), needing no state machine of its own.
- **Wipe trigger is whole-group-down, not any single death** (see
  `DESIGN_IDEAS.md`) — a single player's death needs its own
  in-combat-but-downed state (e.g. incapacitated, awaiting a res/heal)
  distinct from a full wipe; the state machine needs to track
  per-player alive/downed state and only fire the wipe transition when
  all players are downed simultaneously.
- **Taunt** (see `DESIGN_IDEAS.md` — Threat/aggro system) needs the
  threat system to support an explicit override, not just accumulation —
  e.g. a "set to top of threat table" operation an ability can trigger,
  separate from normal damage/healing-based threat generation.
- **Disproportionate threat generation beyond taunt** (see
  `DESIGN_IDEAS.md`) — an ability's threat contribution can't just be
  derived from its damage/healing number; the ability base data needs its
  own explicit threat-generation value (or multiplier), separate from
  the damage/healing value it deals.
- **Threat is per-creature** (see `DESIGN_IDEAS.md`) — each hostile
  creature instance owns its own threat table (player → threat value
  map), not a shared/global one. This lives on the creature, not the
  player or a world-level system.
- **Threat resets on combat-end** (see `DESIGN_IDEAS.md`) — the creature
  needs a combat-state transition (in-combat → out-of-combat) that
  clears its threat table, not just a table that persists for the
  creature's lifetime.

## Loot system

- Each boss needs a **loot table** — a data-driven list of possible item
  drops (fits the same data-driven item model as the gear/ability system
  above), plus however many items drop on a kill (see `DESIGN_IDEAS.md`
  for open questions on fixed vs. randomized count/weighting).
- **"Party divides it themselves"** is a product/UX decision, not an
  auto-assignment algorithm — the code doesn't need loot-rule logic
  (need/greed/master-loot rolls), but it does need *some* mechanism for
  turning "N items dropped" into "items end up in specific players'
  inventories," even if the actual decision of who-gets-what is left
  to the players.
- **Distribution mechanism: shared loot window** (see `DESIGN_IDEAS.md`)
  — on boss kill, dropped items are held in shared/unassigned state
  (visible to the whole party, owned by no one yet) until a player
  assigns each one to a specific party member through a synced UI. No
  player-to-player trading system is needed — assignment is a
  server-authoritative action (any party member → assign item X to
  player Y) rather than an item changing hands after the fact.

## Gear slots

Each character has exactly one slot of each of the following types, and
can equip at most **one item per slot**:

- Helmet
- Necklace
- Chest
- Cape
- Gloves
- Legs
- Boots
- Ring x2 (interchangeable — any ring can go in either slot; decided
  2026-09-11, reduced from the original 4)
- Trinket
- Main hand
- Off hand

That's **12 gear slots** per character.

- **Two-handed weapons**: a two-handed main-hand item occupies both the
  main hand and off hand slots, locking out off hand while equipped —
  main hand/off hand are not always independent. Implies the equip
  logic needs to handle one item claiming two slots (and freeing both on
  unequip), not a strict 1 item ↔ 1 slot mapping.

Open questions:
- Items are slot-typed (a ring can only go in a ring slot, etc.) — any
  planned exceptions to that, or is it a hard rule?

## Open questions / to flesh out

- What are the base characteristics of a piece of gear (e.g. stat block,
  slot type, rarity, on-equip effects)?
- Ability loadout is capped at up to 10 chosen abilities per player (see
  `DESIGN_IDEAS.md` pregame menu) — implies an "ability bar"/hotbar-style
  data structure of fixed size 10 to design around.
- **Hotkeys are player-assigned** (see `DESIGN_IDEAS.md`) — the client
  needs a rebindable keybinding layer mapping key → ability-slot-index,
  stored as part of the player's local/account settings, separate from
  which abilities occupy those 10 slots (chosen in the pregame menu).
- Networking implication: equipped loadout is part of a player's
  synced state — how is that represented/replicated under
  Netcode for GameObjects (see `CLAUDE.md`)?

## Built so far (2026-09-11 refactor) — how the code now realises the above

- Authority split: owner-authoritative movement (`NetworkTransform`
  Owner mode), server-authoritative everything else. See `CLAUDE.md`.
- Data-driven content with stable string `Id`s discovered by
  `GameDatabase` from `Resources/Data`; Ids cross the network and are
  saved, never list indices. Satisfies the "adding an item/ability should
  not require touching character code" requirement above.
- `StatBonus` is the shared "effect expressed as a modifier against a
  stat" primitive for both gear (`ItemData.Bonuses`) and status effects
  (`StatusEffectData.Modifiers`), plugging into `Stat`/`StatModifier`.
- `CharacterStats.ReceiveHit(HitInfo)` is the single damage pipeline
  (armor mitigation, threat, extra threat, effect application). Taunt's
  "set to top of threat" override and the combat-end threat reset are the
  next things to add there / in `ThreatTable`.
- `StatusEffectTracker` (pure C#, tested) owns refresh/tick/expiry rules;
  `TargetSelector` (pure C#, tested) owns enemy targeting modes.
- `PlayerProfile` (JSON in `persistentDataPath`) is the local precursor of
  the account-backed profile: slot Ids + hotkeys, gear Ids, movement
  keys, UI scale. The account/unlock layer should wrap this, not replace
  its shape.

## Change log

- 2026-09-07: Initial capture — modularity requirement for gear/abilities,
  full gear slot list (15 slots: helmet, necklace, chest, cape, gloves,
  belt, legs, boots, 4 rings, trinket, main hand, off hand).
- 2026-09-07: Added character base stats (health, mana, run speed) and
  the implication of a modifier-based stat system. Added world/session
  architecture note for the hybrid persistent-overworld + instanced
  content model.
- 2026-09-07: Updated world/session note now that the 6-player cap is
  server-wide (simplifies instancing — no multi-tenant concern). Added
  class/role note (no class data model needed) and encounter/wipe state
  note (state machine for instanced boss fights).
- 2026-09-07: Added fixed-stats-per-item note (no roll-generation logic
  needed) and account & character model note (one character per account,
  no character-select flow needed). Clarified overworld death needs no
  state machine (plain respawn).
- 2026-09-07: Added mana-only resource note (no resource-type
  abstraction needed), overworld-as-small-lobby note (reduces scene
  scope), portal-based instance transition note, and taunt/threat
  override note.
- 2026-09-07: Added loot system note — boss loot tables reuse the
  data-driven item model; flagged that "party divides freely" still
  needs a distribution/handoff mechanism (world pickups + trading, or a
  shared assignment UI) even without server-side loot rules.
- 2026-09-07: Added gear/ability integration note (gear can grant/modify
  abilities), ability targeting-requirement field note, two-handed
  weapon slot-claiming note, and per-player downed-state note for the
  whole-group-down wipe trigger.
- 2026-09-07: Added per-creature threat table note — threat lives on
  each hostile creature instance, not shared/global.
- 2026-09-07: Added threat-resets-on-combat-end note, resolved loot
  distribution to a shared loot window (server-authoritative assignment,
  no trading system needed), simplified ability targeting-requirement
  field (no hostile/friendly validation needed).
- 2026-09-07: Added health/mana regen note, confirmed run speed is flat
  (Boss 1 scaling is a modifier on that flat value), added
  range/LoS/cooldown as universal ability base-type fields, and
  disproportionate-threat-generation note (ability threat value is
  explicit, not derived from damage/healing).
- 2026-09-07: Added cast-time and mana-cost as per-ability fields on the
  ability base type (cast time can be zero for instant abilities, and
  implies a server-side casting state for nonzero values). Ability base
  characteristics are now essentially fully specified: targeting
  requirement, range, LoS, cooldown, cast time, mana cost, threat value.
- 2026-09-07: Added armor as a character base stat (single % damage
  reduction, no physical/magic split) — simplifies the damage pipeline
  to one mitigation number, no damage-type enum needed.
- 2026-09-07: Confirmed gear can boost max health/mana and their regen
  rates — regen rate itself needs to be a modifiable `Stat`, not a
  hardcoded constant.
- 2026-09-11: Cut ring slots from 4 to 2, and made those 2 interchangeable
  (resolves the open question above) — 15 gear slots become 13.
- 2026-09-12: Removed the Belt slot entirely — 13 gear slots become 12.
