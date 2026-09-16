# Color, contrast, and accessibility

## The rule

**No essential information may be conveyed by color alone.** This isn't an accessibility add-on to
bolt on later — it's a correctness requirement for the base design, because ~8% of men and ~0.5%
of women have some form of color vision deficiency, and every player's HUD sits over a variable,
uncontrolled background that can wash out any single color under the wrong lighting.

## Pairing techniques (use at least one alongside color, for every state-bearing element)

- Text label alongside the color (a number, a name, a short word).
- Shape or icon that's distinct in silhouette, not just fill color (a cross vs. a circle vs. a
  triangle reads the same to every player).
- Pattern or texture (stripes, hatching) layered over a fill color — e.g. a shielded health bar
  segment gets a diagonal-hatch texture in addition to its distinct color.
- Position/grouping — friendly vs. hostile party members in physically separate UI regions, not
  just color-coded within one list.
- Contrast/luminance difference, not just hue difference — a light-vs-dark pairing (e.g. light
  blue vs. dark orange) survives more colorblind conditions than two same-luminance hues.

## Concrete default for encounter

Health/mana/threat-style dichotomies (ally/enemy, ready/on-cooldown, buff/debuff) should default
to a luminance-separated pairing (not two same-brightness hues) plus one of the pairing techniques
above — e.g. party frames already distinguish "your target" via a color shift (green → yellow per
CLAUDE.md); that shift should also change something non-color (an outline, a marker) so it isn't
lost for a colorblind player.

## Contrast ratios (WCAG, adapted)

- Normal text: 4.5:1 minimum contrast against its background.
- Large text and non-text UI (icons, borders of interactive elements, form/slot boundaries): 3:1
  minimum.
- These are *minimums* for static UI on a controlled background — a HUD element that sits over an
  uncontrolled, moving game world should treat these as a floor, not a target, and add a
  shadow/scrim/outline treatment on top (see hierarchy-and-readability.md).

## Colorblind-specific options worth offering (not required for every element, but for the
settings/options surface as a whole)

- Preset filters for protanopia/deuteranopia/tritanopia as toggles, rather than one blanket
  "colorblind mode" — the three conditions need different adjustments.
- Letting the player pick their own color for critical markers (own-target highlight, hostile
  indicator) from a wide swatch, rather than only offering fixed presets — covers severity
  variation within a single condition.
- Avoid full-screen palette-shifting filters as the *only* option — they're a blunt instrument that
  degrades the art direction and still misses edge cases; prefer targeted fixes to the specific
  elements above.

## Validation

Before shipping a color choice for anything state-bearing, check it with a colorblindness simulator
at full (100%) severity, not just a light adjustment — free tools exist (Color Oracle, browser-based
simulators). A design that only "mostly" survives full-severity simulation needs a non-color backup
signal, not a slightly different color.
