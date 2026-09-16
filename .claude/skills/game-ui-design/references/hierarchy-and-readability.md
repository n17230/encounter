# Visual hierarchy, readability, and interaction feedback

## The core constraint: HUDs are read peripherally

A player's focus is on the center of the screen and on the threat, not on the UI. HUD elements
sit at the edges and must communicate **at a glance**, in peripheral vision — which has lower
acuity than foveal (direct-gaze) vision but is more sensitive to motion and contrast than to fine
detail or text. Practical consequences:

- Communicate through shape, size, and motion before text. A player should be able to tell "my
  health is low" from silhouette/color/fill-level alone, without reading a number.
- Apply a consistent visual treatment (drop shadow, scrim, or outline) so text/icons survive both
  bright and dark backgrounds — a HUD that looks fine in a static mockup can become unreadable
  over the actual game world.
- Anchor elements in **stable positions**. Don't move or resize a HUD element based on state
  changes the player didn't cause (e.g. don't let the ability bar grow/shrink as abilities go on
  cooldown) — position stability is itself part of readability, since the eye learns where to look.
- Test against real gameplay captures with VFX, camera shake, and motion — not a static screenshot
  — before calling a layout done.

## Urgency-based hierarchy

Rank every HUD element by urgency and let that rank drive contrast, size, and position stability,
not aesthetic preference:

1. **Life-critical** — own health, incoming lethal danger. Strongest contrast, most stable
   position, largest relative size, never obstructed by other UI.
2. **Actionable** — ability cooldowns, current target's health, an active cast bar. High contrast,
   stable position, sized for at-a-glance reading.
3. **Situational** — party/ally status, minimap, buffs/debuffs on self or target. Present but
   quieter; can tolerate slightly more visual competition.
4. **Ambient/cosmetic** — currency, non-urgent progress, low-frequency counters. Lowest contrast,
   can be tucked into a corner or made available on demand rather than always-on.

For encounter specifically: own health/mana (life-critical/actionable) and the ability-cooldown
bar (actionable) should never lose the contrast fight to party frames or the minimap (situational).

## Diegetic / non-diegetic / spatial / meta

Framework from Fagerholt & Lorentzon, *Beyond the HUD: User Interfaces for Increased Player
Immersion in FPS Games* (2009), now standard across the field:

- **Diegetic** — exists in the game world, the character can perceive it (an in-world phone
  screen, a readable sign). Increases immersion but costs legibility/consistency since it's
  subject to the world's lighting, distance, and camera angle.
- **Non-diegetic** — exists only for the player, not the character (health bars, menus, minimaps).
  Most reliable for legibility and consistency; the default choice for anything the player must be
  able to read instantly and consistently, which is why it dominates HUDs in this genre.
  Most of encounter's UI is and should be non-diegetic.
- **Spatial** — rendered in/over the game world but not perceived by the character (an enemy
  outline, a ground-targeting reticle, a cast range indicator). Good for information that's
  inherently about world-space (encounter's `GroundTargetReticle` and target ring indicators
  already work this way).
- **Meta** — represents a character/game state without a spatial or object anchor, usually a
  full-screen effect (a red vignette at low health, a desaturation on a debuff). Good for visceral,
  hard-to-miss state changes, but shouldn't be the *only* way critical information is conveyed
  (see color-and-accessibility.md — same "don't rely on one channel" principle applies to meta
  effects, since a player who's tuned out screen tinting from a previous game needs a backup
  signal).

Most well-regarded UI combines these deliberately rather than defaulting to one: diegetic/spatial
for world-grounded, atmospheric information; non-diegetic for dense or precise data the player
must parse reliably; meta for emotional/state punctuation layered on top of (not instead of) a
non-diegetic readout.

## Affordance and feedback

- **Affordance**: a control should look usable before it's touched. A signifier (shape, shadow,
  hover-state, label, cursor change) communicates this — a flat-colored rectangle with no depth
  cue doesn't read as a button.
- **Feedback**: every player action gets an immediate, legible reaction — a state change, a brief
  animation, or a sound, ideally more than one of these together. Feedback confirms the action was
  *received*, independent of whether the action *succeeded* (compare: encounter's client-side
  cast prediction already separates "the client acknowledged your key press" from "the server
  accepted the cast" — the UI should keep that same distinction legible, not collapse it).
- **Cooldowns specifically**: players need to (1) release an ability quickly with minimal
  attention, and (2) know at a glance when it's available again. A radial or linear sweep that
  empties as the cooldown completes, plus a distinct "ready" flash/pulse at completion, outperforms
  a bare numeric countdown for glance-speed reading — numbers support the sweep, they shouldn't
  replace it.
