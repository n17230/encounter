# reallyfungame — Design Ideas

Living document for gameplay/design decisions, separate from `CLAUDE.md`
(which tracks technical/engineering setup). Add to this freely as ideas
come up; doesn't need to be polished, just captured.

## Core concept

- **Open world, 3D**, played together with friends.
- Up to **6 players**, and this is the **whole server's hard cap** — not
  a per-group/instance limit, the server only ever supports 6 concurrent
  players total (matches the dedicated-server / server-authoritative
  direction in `CLAUDE.md`). Simplifies world/instance architecture since
  the server population and the max group size are effectively the same
  number.
- Each player controls **their own character** moving around the shared
  open world (not a shared/rts-style camera — individual avatars).
- **No classes** — fully freeform. Any player can build any combination
  of gear/abilities; roles like "tank" emerge from loadout choice, not
  from a locked class. See Class/role system below.
- **Camera**: third-person, over-the-shoulder — matches the WoW-style
  controls/targeting already decided.
- **One character per account** — no character-select screen, the
  account IS the character.
- **World structure**: hybrid — a **persistent shared overworld** players
  are in together, with **instanced content** (boss fights, dungeons)
  split off into their own sessions. Matches the boss designs in
  `BOSS_DESIGN.md` and the threat system, which are encounter-shaped
  rather than open-world-shaped.
  - The persistent overworld is **small** — essentially a **social
    lobby/hub area**, not a large explorable world with its own content.
    Its purpose is to be the shared space players are in together
    between instances, not a destination in itself.
  - Players **connect to instanced content via a portal** in the
    overworld — a physical thing players interact with together to
    enter an instance, not a matchmaking queue/menu.
- **Primary gameplay loop**: **combat/PvE focus** — gear/ability
  loadouts, threat system, and boss encounters are the core loop for now.
  Crafting/building/etc. are not in scope unless revisited later.

## Accounts & persistence

- Players have **stateful accounts** — progress persists across sessions,
  not tied to a single play session or a single server run.
- **Items/inventory persist** — anything a player acquires stays with
  their account.
- **Auth method**: simple **username + password**, stored/checked on the
  self-hosted server — no third-party identity provider (Steam, etc.).
- Implies (not yet decided, flagging for later):
  - Where account data lives — flat files on the server vs. a database
    (SQLite to start is plausible for a friends-scale game).
  - Password storage must be hashed (not plaintext) even for a small
    friends-scale server.

## Items & progression

- **No item restrictions** — any character can use any item. No level
  requirements, no class/role locks on gear.
- Implication: progression is likely driven by *acquiring* items rather
  than *unlocking the ability to use* items — power comes from what you
  find/earn, not from gating existing items behind a leveling system.
- **Starting set**: every account begins with a baseline set of abilities
  and gear.
- **Unlocks**: additional abilities and gear get unlocked through game
  progression — once unlocked, they're usable with no further
  restriction (consistent with "no item restrictions" above).
- **No player levels** — progression is entirely about *unlocking* items
  and abilities, not leveling up a character. There's no character-level
  stat that gates or scales anything; power comes purely from which
  items/abilities have been unlocked and equipped.
- Gear slots and the modularity requirement this creates for the codebase
  are tracked separately in `ARCHITECTURE_NOTES.md`.
- **Itemization**: fixed stats per item — every instance of a given item
  is identical, no random stat rolls or rarity-tier variance (v1;
  rarity/randomization could be revisited later).
- **Gear can grant/modify abilities** — equipped gear isn't purely stat
  bonuses; some gear (e.g. a specific weapon or trinket) can grant a new
  ability or change how an existing one behaves. This couples the gear
  and ability systems together rather than keeping them fully
  independent — see `ARCHITECTURE_NOTES.md`.
- **Gear can add health, mana, or regen of either** — beyond armor,
  equipped gear can directly boost max health, max mana, health regen
  rate, or mana regen rate.
- **Two-handed weapons**: a two-handed main-hand weapon occupies both the
  main hand and off hand slots (locks out off hand) — main hand/off hand
  aren't always independent.

## Pregame menu / loadout selection

- Before entering the world/session, players use a **pregame menu** to
  choose their **loadout**: what gear to have equipped and which
  abilities to bring in.
- Ties into items & progression above — since any character can use any
  item (no restrictions), the pregame menu is where players pick *which*
  of their acquired items/abilities to actually bring, rather than being
  locked into a fixed kit.
- **Ability limit**: players can bring up to **10 spells/abilities** per
  loadout. (Gear slot count is fixed at 15 — see `ARCHITECTURE_NOTES.md`
  — but ability count is a separate, chosen-up-to-10 limit.)
- **Loadout timing**: locked in before entering — set via the pregame
  menu, fixed for the duration of that session (no mid-session
  swapping in v1).
- **Hotkey assignment**: players assign their chosen abilities to
  hotkeys themselves — which key triggers which of their up-to-10
  abilities is player choice, not a fixed/default mapping.
- Open questions:
  - Is the gear loadout also capped by choice (within the 15 fixed
    slots), or is gear just "whatever's equipped in each of the 15
    slots"? (Abilities are the ones being explicitly capped at 10.)
  - Does the pregame menu show only items the player owns/has unlocked
    (tied to their stateful account), or the full item pool?

## Character base stats

- Every character has, at minimum, these base properties:
  - **Health**
  - **Mana**
  - **Run speed**
  - **Armor** — see Defense below.
- These are the baseline stats gear/abilities/boss effects will modify
  (e.g. Boss 1's speed-scales-with-DPS-taken effect modifies run speed;
  gear/abilities presumably grant bonuses to health/mana/run speed).
- Tracked alongside the gear/ability base-characteristics work in
  `ARCHITECTURE_NOTES.md` — this is the character-level counterpart to
  that.
- **Mana is the only resource** — no per-character/per-ability resource
  types (no energy, rage, etc.) — every ability draws from the same mana
  pool.
- **Health and mana both regenerate** over time (passive regen, not
  purely restored via abilities/items).
- **Run speed is flat** — a single flat value, not a base + multiplier
  system. (Effects like Boss 1's DPS-taken speed scaling still apply as
  modifiers on top of that flat value — see `ARCHITECTURE_NOTES.md`.)
- **Defense**: a single **armor stat = a % damage reduction**. No
  physical vs. magic damage-type split — one flat mitigation number
  reduces all incoming damage, regardless of source.
- Open questions:
  - Any other base stats expected soon (e.g. crit chance)? Called out
    now since the data model will want to be extensible for this.
  - What are the regen rates for health/mana, and do they scale with
    anything (e.g. gear, being in/out of combat)?

## Controls / movement scheme

- **Forward movement**: `W`, or hold left click + right click together to
  run forward.
- **Backward movement**: `S`.
- **Strafe**: `A` / `D` strafe left/right.
- **Escape menu**: `Escape` in the testing area opens a pause-style menu
  with the Skills and Gear pickers plus a Keybindings page where all of
  the movement keys above (and Jump / Auto-Run) can be rebound.
- **Look direction**: controlled by the mouse whenever right click is
  held (including while moving forward, since forward requires right
  click to be held anyway).
- **PC only** — no controller/gamepad support planned. Mouse + keyboard
  only, consistent with the click/mouse-look-driven control scheme.
- **Left click alone**: UI interaction only — no combat action of its own
  (no basic/auto attack). Used for clicking targets, UI elements, and
  world objects like the portal; combat is done entirely through the
  ability bar (the up-to-10 chosen abilities).

## Targeting

- **WoW-style targeting**: players select a single target, and
  spells/abilities are cast at that target (as opposed to skillshot/
  action-combat aiming).
- **Target selection**: both click-to-target and tab-target cycling are
  supported.
- **Mixed targeting requirements**: most abilities are single-target, but
  some abilities need no target at all — self-cast buffs/heals,
  ground-targeted AoE, etc. Each ability's data needs a targeting-
  requirement field rather than assuming every ability needs a target.
- **No hostile/friendly restriction** — the game doesn't enforce target
  validity by ability type; any ability can be cast on any target
  regardless of hostile/friendly (e.g. a heal could technically be cast
  on an enemy, a damage spell on an ally). Simpler rule, no per-ability
  target-type validation needed.
- **Range matters for all spells/abilities** — every ability has a range
  requirement; it can't be cast on a target that's too far away.
- **Line of sight is required to attack** — a target must be in line of
  sight; abilities can't be cast through walls/obstructions.
- **Spells have cooldowns** — abilities are limited by per-ability
  cooldowns, not just mana cost.
- **Cast time varies per ability** — some abilities are instant, others
  have a cast time (a delay before the effect resolves, interruptible in
  principle).
- **Mana cost varies per ability** — not a flat cost across all
  abilities; each ability defines its own mana cost.

## Class / role system

- **Freeform, no classes** — there's no locked-in class restricting which
  abilities/gear a player can use. A player's "role" (tank, healer,
  damage) emerges entirely from which of their unlocked
  gear/abilities they choose to bring in the pregame menu.
- Consistent with "no item restrictions" in Items & progression — the
  same philosophy extends to abilities: nothing is gated by class,
  everything is gated by what's been unlocked on the account.
- Architecture implication: no class data model needed — just the
  gear/ability modularity system already tracked in
  `ARCHITECTURE_NOTES.md`. Simpler than a class-gated design.
- **No role signaling** — there's no in-game UI for indicating/agreeing
  who's tanking, healing, etc. before a pull. That coordination is left
  entirely to player communication (voice/text outside or alongside the
  game), not a system the game needs to build.

## Death & failure states

- In **instanced content** (boss fights): death causes a **wipe /
  encounter reset** — the encounter fails and restarts, WoW-raid style,
  rather than a per-character respawn-and-continue.
- **Wipe trigger**: only the whole group being downed triggers a wipe —
  a single player's death is recoverable (e.g. via a res/heal), not an
  automatic reset.
- **Overworld death**: simple respawn, no penalty — respawns at a set
  point (or last-visited location) with no lasting penalty (v1;
  penalties could be revisited later).
- Open questions:
  - Is there any cost to wiping (e.g. losing progress within that
    encounter attempt, a cooldown before re-entering), or is it a clean
    restart?

## Loot

- Bosses have a **loot table** — a defined pool of possible item drops.
- On kill, the boss drops **some number of items** from that table.
- **The party divides the loot themselves** — the game doesn't enforce
  who gets what; players have freedom to decide amongst themselves how
  drops are split up.
- **Distribution mechanism**: a **shared loot window** — after a kill, all
  dropped items are shown to the whole party in a shared UI, and any
  player can claim/assign an item to a specific party member directly
  from that window. No separate player-to-player trading system is
  needed for this — assignment happens through the loot window itself,
  not by trading already-owned items around afterward.
- Open questions:
  - Is the drop count fixed per boss, or randomized within a range?
  - Are loot table entries equally likely, or weighted (e.g. rarer items
    drop less often)?

## PvP

- **Optional/casual PvP** exists as a side feature — not a core pillar of
  the game, which remains primarily PvE (threat system, boss encounters).
- Open questions:
  - What form does it take — dueling, a dedicated arena, or something
    else?
  - Does it use the same freeform gear/ability rules as PvE, or does it
    need separate balance considerations?
  - Given the persistent-account item system, is there any risk (e.g.
    item loss) tied to PvP, or is it purely for fun with no stakes?

## Threat / aggro system

- There will be a **threat system** — enemies track threat/aggro per
  player, and players can actively influence it (tanks generating threat
  to hold aggro, others managing their own threat to avoid pulling it).
- Ties directly into `BOSS_DESIGN.md` Boss 2 ("unconventional tank"
  concept) — a real threat system is what makes tanking a distinct,
  learnable role rather than just "whoever has the most HP."
- **Taunt mechanic**: yes — there will be a taunt ability/effect that
  forces/resets top-of-threat onto the taunting player.
- **Threat is per-creature** — each hostile creature tracks its own
  independent threat table, not a shared/global one. Taunting or
  building threat on one enemy has no effect on another's threat table.
- **Threat resets when combat ends** — once a creature fully disengages/
  resets out of combat, its threat table clears; the next pull starts
  fresh rather than threat carrying over from a prior, interrupted
  engagement.
- **Disproportionate threat generation**: yes — beyond taunt, some
  abilities generate threat disproportionate to the damage/healing they
  do (threat-boosting abilities/effects), not purely a 1:1 ratio with
  damage or healing dealt.

## Change log

- 2026-09-07: Initial capture — open world 3D, up to 6 players, each
  controls own character, stateful accounts with persistent items.
- 2026-09-07: Added items & progression — no item restrictions, any
  character can use any item, no level requirements.
- 2026-09-07: Added pregame menu — players choose equipped gear and
  abilities to bring before entering the session.
- 2026-09-07: Clarified starting set — accounts begin with baseline
  abilities/gear, more unlocked via game progression.
- 2026-09-07: Added controls/movement scheme — hold L+R click to run
  forward, A/D to strafe, mouse-look active while right click held.
- 2026-09-07: Added targeting — WoW-style single-target selection,
  abilities cast at current target.
- 2026-09-07: Set ability loadout limit — up to 10 spells/abilities per
  loadout.
- 2026-09-07: Added threat/aggro system — players can control aggro on
  enemies.
- 2026-09-07: Resolved pre-start questions — hybrid persistent overworld
  + instanced encounters, combat/PvE-focused core loop, username+password
  auth, loadout locked in before entering (no mid-session changes in v1).
- 2026-09-07: Added character base stats — health, mana, run speed.
- 2026-09-07: Resolved second round of pre-start questions — no class
  system (fully freeform), 6-player server-wide hard cap, death in
  instanced content causes a wipe/encounter reset, PvP exists as an
  optional/casual side feature.
- 2026-09-07: Resolved third round of pre-start questions — third-person
  over-the-shoulder camera, fixed stats per item (no random rolls/rarity
  in v1), one character per account, overworld death is a simple
  no-penalty respawn.
- 2026-09-07: Resolved fourth round of pre-start questions — mana is the
  only resource, taunt mechanic confirmed, targeting supports both click
  and tab-target, no backward movement key, persistent overworld is a
  small social lobby/hub (not explorable content) connecting to instances
  via a portal.
- 2026-09-07: Added loot system — bosses have a loot table, drop some
  number of items on kill, party divides loot freely (game doesn't
  enforce distribution).
- 2026-09-07: Resolved fifth round of pre-start questions — mixed
  targeting (some abilities need no target), gear can grant/modify
  abilities, two-handed weapons occupy both weapon slots, wipes only
  trigger on whole-group-down (not any single death).
- 2026-09-07: Resolved sixth round of pre-start questions — PC only, no
  controller support; no in-game role signaling (left to player
  communication); no player levels, progression is purely about
  unlocking items/abilities.
- 2026-09-07: Clarified threat is per-creature — each hostile creature
  has its own independent threat table.
- 2026-09-07: Resolved seventh round of pre-start questions — left click
  alone is UI-only (no basic attack), no hostile/friendly target
  restriction, loot distributed via a shared loot window (no trading
  system needed), threat resets when combat ends.
- 2026-09-07: Resolved eighth round of pre-start questions — health and
  mana both regenerate, run speed is a flat value, range and line-of-
  sight matter for all abilities, spells have cooldowns, disproportionate
  threat-generating abilities exist beyond taunt.
- 2026-09-07: Added ability cast-time and mana-cost variance — some
  abilities are instant, some have cast time; mana cost varies per
  ability.
- 2026-09-07: Added armor/defense — single armor stat = % damage
  reduction, no physical/magic damage-type split.
- 2026-09-07: Confirmed gear can grant bonuses to health, mana, and
  health/mana regen rate (not just armor).
- 2026-09-07: Added hotkey assignment — players assign their chosen
  abilities to hotkeys themselves, no fixed mapping.
