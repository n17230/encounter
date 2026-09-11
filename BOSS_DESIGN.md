# reallyfungame — Boss Design Ideas

Living document for boss encounter concepts, separate from
`DESIGN_IDEAS.md` (general game design) and `CLAUDE.md` (technical setup).

## Boss 1 — "Enrage on Damage Taken" (working name)

- Gains a **movement speed bonus that scales with incoming DPS** — the
  more damage he's taking, the faster he moves.
- Effect: punishes players for burning him down too aggressively without
  a plan for handling a faster/harder-to-kite boss; likely intended to
  create a damage-pacing or kiting puzzle rather than a pure DPS race.
- Open questions:
  - Is the speed scaling based on instantaneous DPS (rolling window) or
    total damage taken so far?
  - Is there a cap on max speed, or does it scale unbounded?
  - Does speed decay over time if DPS drops (e.g. players back off)?
  - Does this affect only chase/movement speed, or also attack
    speed/animation speed?

## Boss 2 — "Life Drain / %HP Damage" (working name)

- Deals **% max HP damage** with her attacks.
- **Heals for a percentage of the damage she deals** (lifesteal-style).
- Design intent: forces an **unconventional tank** — a flat-HP stacking
  tank doesn't work well against %HP damage, so the encounter should push
  players toward a different mitigation strategy (damage reduction,
  healing-per-second, avoidance, or a tank build not normally viable).
- Open questions:
  - Is the %HP damage based on the target's max HP or current HP?
  - Does the healing scale off damage dealt to the tank specifically, or
    all damage dealt (including to multiple targets/AoE)?
  - Is there a way to counter the healing (e.g. anti-heal/healing
    reduction effects), or is out-mitigating the %HP damage the only
    counterplay?
  - What "unconventional tank" archetypes exist in the game's kit that
    this boss is meant to showcase?

## Open questions / to flesh out

- How many bosses total, and do they share a common structure (phases,
  enrage timers, arena mechanics)?
- Are bosses tuned around the full 6-player group, or scalable for fewer
  players?
- Loot/reward hooks tying back into the stateful account/item system in
  `DESIGN_IDEAS.md`.

## Change log

- 2026-09-07: Initial capture — Boss 1 (speed scales with DPS taken) and
  Boss 2 (%HP damage + damage-based self-heal, unconventional tank check).
