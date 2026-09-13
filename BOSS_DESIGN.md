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

## Boss 3 — "Skeleton Tactician" escort encounter

A five-mob encounter: **the Tactician is the boss**, protected by four
skeleton escorts (Warrior, Archer, Healer, Mage) who fight as a
coordinated unit for as long as the Tactician is alive.

- **The Tactician avoids melee.** It opens the fight as a kiting ranged
  attacker, always trying to keep its distance from players, protected
  behind its escort. It's only forced into melee once a player directly
  engages it, or gets within 15 yards of it, or the escort is mostly
  dead (fewer than 3 skeletons left) — at which point it stops kiting
  and just fights.
- **While the Tactician is alive, it broadcasts a coordination aura**
  ("Tactical Instruction") to the rest of the escort: the Warriors focus
  whoever's threatening the Tactician, the Mage focuses whoever's
  closest to the Archers, the Archers focus whoever's farthest away, and
  the whole escort gets a mana-regen boost plus immunity to slow effects
  from ice spells. **The instant the Tactician dies, all of that goes
  away** — the surviving escort reverts to plain threat-table AI, no
  more coordinated targeting, no more mana sustain. This is the
  fight's central tension: burn the Tactician first to make the escort
  fall apart into disorganized trash, or clear the escort first to strip
  its protection and force it into melee — either path works, they're
  just different fights.
- **Warrior**: the melee anchor. Tanky (copies the Goblin Brute's
  stats), stuns periodically, and goes into a temporary enrage
  (more damage, more armor, a heal) whenever a player gets close to the
  Tactician — it's the thing punishing players for getting aggressive
  toward the boss while the escort's still up.
- **Archer**: ranged DPS. Slows whatever it's shooting, and protects
  itself by falling back behind the Warriors (without losing its own
  target) whenever the Mage has to intervene on its behalf. Also
  punishes low-health targets with a follow-up shot.
- **Healer**: pure support, no weapon of its own — keeps the escort
  topped off with a heal-over-time and an emergency burst heal, and can
  drain a player's mana to refill its own when it runs low.
  Deliberately the priority-target of the group if left alone.
- **Mage**: ranged caster and the escort's utility piece. Blinds
  whoever's threatening an Archer, throws the player's own Icebolt
  spell, and drops a protective dome around any escort member who's
  dropped low that blocks ranged damage from reaching them.
- **The "blinding" debuffs (Enshroud, Concussive Shot) are a new kind of
  effect for the game** — they don't just apply a stat penalty, they
  actually darken the affected player's own screen, so they can only
  see what's within a shortened vision range. Purely a per-player visual
  effect; nobody else's view is affected.

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
- 2026-09-13: Boss 3 captured — the Skeleton Tactician escort encounter,
  fully designed (five mobs, coordination aura, vision-darkening debuffs);
  implementation in progress.
