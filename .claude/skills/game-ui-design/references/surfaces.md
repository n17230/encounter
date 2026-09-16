# Per-surface checklists for encounter's actual UI

Each checklist assumes the four-question method (SKILL.md) has already been run. These are the
surfaces named in CLAUDE.md's UI section — don't invent surfaces beyond these without the user
asking.

## Party frames / unit frames (`PartyFrames.cs`, health+mana rows, top-right)

- Urgency tier: situational normally, but the *player's own* health/mana is life-critical and
  should not live only inside this list — it needs its own higher-contrast, more stable treatment
  (WoW's convention of a distinct, larger player frame separate from the party list is the genre
  precedent here, not an arbitrary choice).
- Ordering must be stable across frames for the same viewer (encounter already guarantees this via
  `PartyNumber`/`OwnerClientId` sort — don't propose a UI change that would make row order jump
  around as players' health changes, that breaks muscle memory).
- Current-target highlight (existing green→yellow shift) needs a non-color companion per
  color-and-accessibility.md — an outline or marker glyph, not color alone.
- Health/mana bars: fill-direction and empty-state must be legible without reading a number —
  segment/tick marks at meaningful thresholds (25/50/75%) help at-a-glance reading during fast
  combat.

## Minimap + fog-of-war-style reveal (`Minimap.cs`, circular, north-up, bottom-right)

- This is non-diegetic and should stay that way — a minimap is exactly the case where reliable,
  consistent legibility beats immersion.
- Reveal state (per CLAUDE.md: blank by default, gear-gated reveals, mob pings that fade) should
  visually distinguish three states, not two: never-revealed (blank), revealed-but-stale (a mob
  ping that's faded past its window), and currently-live — collapsing stale/live into the same
  visual treatment reads as a bug ("why did that blip disappear/reappear").
- Player-vs-target-vs-ally-vs-hostile blips need shape or size differentiation in addition to
  color (existing green/yellow/red scheme) — same color-alone rule as party frames.
- The two reference rings (50/100 marks, per CLAUDE.md) are a good existing pattern for giving
  distance an at-a-glance anchor without requiring numeric readout — keep this kind of ambient
  scale cue in any minimap redesign.

## Ability bar + cooldowns (predicted cooldowns, global cooldown, per CLAUDE.md)

- Cooldown sweep (radial or linear fill) is the primary readout; a numeric countdown is secondary,
  layered on top, not a replacement (see hierarchy-and-readability.md).
- Client-predicted cast vs. server-confirmed cast are two different states this project already
  models in code (`PlayerAbilities`'s rollback-on-rejection) — the UI must keep them visually
  distinct (e.g. a predicted-but-unconfirmed cooldown reads differently than a
  server-confirmed one) rather than showing one ambiguous "casting" state, so a rejected cast's
  rollback doesn't look like a UI glitch.
- Global cooldown (1.5s, shared across all slots) needs its own subtle, shared visual cue distinct
  from any single ability's own cooldown sweep — otherwise a GCD-blocked cast looks identical to a
  same-ability recast being blocked, which are different failure reasons a player should be able
  to tell apart.
- "Always On" aura-spell slots (no cast, no cooldown, per CLAUDE.md) need a visual treatment that's
  unambiguously *not* a cooldown-capable slot — a static "equipped" indicator rather than an empty
  cooldown ring, so it doesn't read as broken/stuck.

## Gear paper-doll + inventory grid (`MainMenu.DrawGearPanel`, and the character-appearance panel)

- Paper-doll layout convention: equipment slots arranged around a body silhouette in roughly their
  worn position (head top, boots bottom, weapon hand-side) reads faster than a flat alphabetical
  grid — encounter's existing 3-column slot grid is a reasonable placeholder but a body-silhouette
  arrangement is the stronger long-term target once there's art.
- Slot-category color outlines (existing pattern: ring=red, trinket=green, etc.) are decorative
  categorization, not state — fine to keep color-only here since nothing critical depends on
  reading the outline color specifically (the slot's fixed position and label already disambiguate
  it), but don't let this pattern spread to anything where color *is* the only cue for a real
  state.
- Tooltip-on-hover for every slot/inventory item is load-bearing here (no other way to identify an
  "X" placeholder) — see the tooltip checklist below; this is the single highest-traffic tooltip
  surface in the game right now.
- Character-appearance panel (Top/Bottom/Headwear lists + two color steppers): selection state
  (`"> "` prefix convention) should eventually become a real selected-state visual treatment
  (border/highlight), not rely on a text prefix once there's real UI — text-prefix selection is a
  placeholder-IMGUI compromise, not a design target.

## Tooltips

- Keep item/ability tooltips to roughly 3-4 lines for the default/glance view; anything longer
  (encounter's full stat-bonus + immunity + aura breakdown, per `MainMenu.BuildItemTooltip`) should
  be available but not forced on every hover — consider a "hold to expand" or always-expanded
  choice deliberately, not by default just because the data exists.
- Combat-relevant tooltips (a target's active status effects, an ability's cooldown mid-fight)
  should be shorter and faster-appearing than out-of-combat/menu tooltips (a full item tooltip in
  the Gear menu) — different urgency, different tolerance for tooltip latency/length.
- Numbers and keywords that matter for a decision (damage, mana cost, "Immune to Slow") should be
  visually distinguished from flavor/descriptive text within the same tooltip, not uniform body
  text.

## Status effect icons (`ActiveEffectNet`, `PlayerHUD` "Burning 2.3s" text, per CLAUDE.md)

- Icon + remaining-duration readout together, not text-only ("Burning 2.3s" as plain text is a
  placeholder; a real version needs an icon that reads at hotbar size plus a duration sweep/number,
  same sweep-then-number pattern as ability cooldowns for consistency across the whole HUD).
- Buff vs. debuff needs a non-color-only distinction (border shape or icon frame style, not just a
  green/red tint) per color-and-accessibility.md.
- `EffectStackingMode.StackUpToLimit` effects (e.g. Armorbreaker's Sunder) need a stack-count
  readout on the icon itself once there's real art — a single icon with no stack number can't
  communicate "this is applied 3 times" as anything but a duration bar, which understates the
  actual game state.
- Known project limitation (per CLAUDE.md): two simultaneous `StackPerCaster` instances of the same
  effect currently collapse to one HUD entry. A UI redesign shouldn't silently paper over this by
  assuming one-icon-per-effect-Id is always correct — flag it if a proposed design depends on that
  assumption.

## Floating combat text / damage numbers (not yet implemented, per CLAUDE.md's "combat log, damage
numbers" future item)

- Normal hits: small, clean, uniform (white or near-white, light outline for background
  independence) — deliberately unremarkable, since hundreds will appear during sustained combat
  and each one being "exciting" causes clutter, not clarity.
- Critical/notable hits: differentiate by size (roughly 150-200% of normal) and a brief scale-up
  animation, not primarily by color — color can reinforce, shouldn't carry it alone.
- Damage-type channels (fire/poison/bleed/heal/shield, all of which exist in encounter's
  `HitInfo`/`StatusEffectData` model) each want a consistent color+icon pairing used everywhere
  that type appears (tooltip, status icon, floating number) — establish the mapping once, reuse it,
  don't let each surface invent its own.
- Spawn-position jitter (small random offset per number) prevents stacked hits from becoming an
  unreadable pile during AoE/multi-hit sequences — necessary for this project given AoE abilities
  (Cleave, Seismic Slam, ground patches) that can land many simultaneous hits.

## Cast bars (ground-targeted cast time, `AbilityData.CastTime`)

- Own cast bar: life-critical-adjacent (interrupting your own understanding of "am I still
  casting" matters for input timing) — stable position, high contrast, not shared visual space with
  unrelated elements.
- Target's cast bar (when targeting a casting enemy, e.g. a Skeleton Mage): should visually flag
  interruptible vs. not, if/when that mechanic exists — don't imply interruptibility that isn't
  real.
- A cast bar's fill direction and completion state should be legible without reading a timer
  number, same "shape/motion before text" principle as cooldowns.

## Escape / pregame menu navigation (`MainMenu.cs` panels)

- Panel list (Skills/Gear/Character Creation/Options/Summon) is a simple flat menu — this is
  correctly a non-diegetic, low-stakes surface; don't over-design it relative to how often it's
  used (players open it, pick a panel, leave — optimize for that flow being fast, not for making
  the menu itself a visual centerpiece).
- Every panel needs a consistent "Back" affordance in the same screen position across panels
  (already true) — don't let a redesign make Back's position panel-dependent, that breaks the
  "leave this menu" muscle memory the same way unstable HUD positions do.
