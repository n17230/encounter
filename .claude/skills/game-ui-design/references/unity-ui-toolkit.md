# UI Toolkit implementation notes

For when encounter's UI actually migrates off placeholder IMGUI (`Assets/Scripts/UI/`) — per
CLAUDE.md, "real UI should be UI Toolkit." These are implementation/performance rules, not UX
rules — the visual/UX decisions come from the other reference files first.

## Architecture

- Structure with UXML, style with USS, control with C# — keep these three separated (don't set
  layout/visual properties from C# that a USS class could express, don't put interaction logic in
  UXML/USS). This project's existing IMGUI code mixes layout and logic inherently (that's normal
  for IMGUI) — the migration is a real architectural shift, not a syntax swap.
- Follow BEM (Block/Element/Modifier) for USS class naming: blocks are standalone components
  (`party-frame`, `ability-slot`), elements are a block's children using `__`
  (`ability-slot__cooldown-sweep`), modifiers are state/variant flags using `--`
  (`ability-slot--on-cooldown`, `party-frame--is-target`). This keeps state changes to a single
  class add/remove rather than rewriting inline styles.
- Assign USS classes via `AddToClassList()` in a custom VisualElement's constructor, and
  add/remove modifier classes as state changes (e.g. toggle `--on-cooldown` rather than directly
  poking style properties per-frame).

## Performance rules

- Avoid inline styles (`element.style.X = ...`) for anything applied to many elements — inline
  styles carry per-element memory overhead. Prefer USS classes.
- Selector cost scales roughly with (number of classes) × (applicable USS files) — large lists
  (a raid-frame-style party list, an inventory grid) are exactly where this matters most in this
  project.
- Avoid `:hover` on elements with many descendants — mouse movement invalidates the whole
  hierarchy's styling on every move. Scope `:hover` narrowly (the specific button, not a
  container).
- Prefer child selectors (`>`) over descendant (space) selectors, and avoid the universal selector
  (`*`) at the end of a complex selector or combined with a descendant selector.
- For frequently-recreated elements (floating combat text, status effect icons, party-frame rows
  as players join/leave) — pool and reuse `VisualElement`s instead of creating/destroying them
  every time; reset pooled elements' state before returning them to the pool. This project's
  damage-number and status-icon surfaces (both currently unbuilt) are exactly the
  spawn-frequently case pooling is for.
- Pre-create elements that toggle visibility often (e.g. an ability slot's cooldown overlay) and
  hide/show via a USS class rather than instantiating/destroying them each time.

## Where this maps onto encounter's existing systems

- Party frames, status effect icons, and floating combat text are all "many similar elements,
  created/destroyed at runtime" cases — pool from day one rather than retrofitting later.
- The minimap's blips (player/mob/reveal pings, currently runtime-generated textures per
  CLAUDE.md) are a good candidate for the same pooling treatment once minimap is UI Toolkit — reuse
  blip elements rather than recreating them every ping cycle.
- The ability bar's cooldown sweep is a per-frame-updating visual (not a discrete state change) —
  this is one case where a direct style/property update per frame is appropriate rather than a USS
  class toggle, since the fill amount is continuous, not a fixed set of states.
