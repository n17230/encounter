---
name: game-ui-design
description: Design, critique, or implement a menu or HUD element for encounter (party frames, minimap, ability bar, gear paper-doll, tooltips, status effects, damage numbers, cast bars, Escape/pregame menus). Use whenever asked to design new UI, improve existing UI/UX, pick colors or layout for a HUD element, or write UI Toolkit UXML/USS/C# for this project. Grounded in cited game-UX research, not generic web-design advice.
---

# Game UI design for encounter

This project's UI is currently placeholder IMGUI (`Assets/Scripts/UI/`, deliberately disposable
per CLAUDE.md). Real UI will be built in **UI Toolkit**. This skill is the methodology + checklists
to use whenever that real work happens, or whenever asked to critique/improve the current
placeholder UI's information design even before the UI Toolkit migration.

Do not use this skill to invent new UI screens, HUD elements, or game systems that weren't asked
for — it's a craft skill for surfaces that already exist or were explicitly requested, per this
project's core rule against inventing unrequested content.

## The four-question method

For any UI element (new or existing), answer these in order before touching layout or color:

1. **What is it, diegetically?** Diegetic (in-world, character can see it) / non-diegetic (player
   only) / spatial (in-world but character-blind, e.g. an enemy outline) / meta (screen-space
   effect representing a character state, e.g. a red vignette at low health). See
   `references/hierarchy-and-readability.md`. Almost everything in encounter is non-diegetic
   (health bars, ability bar, minimap) — that's fine and expected for this genre; only flag it
   when something could read better as spatial/meta (e.g. a status effect that's also visible as
   a particle on the character, which this project already partly does — see
   `EffectOverheadVisual`/`AuraGroundVisual` in CLAUDE.md).
2. **What's its urgency tier?** Life-critical (own health, incoming danger) > actionable
   (cooldowns, target health, cast bars) > situational (party status, minimap) > ambient (currency,
   cosmetic state). Urgency tier drives position stability, size, and contrast — see
   `references/hierarchy-and-readability.md`.
3. **Does it survive on a busy background, and without color?** Every element that reads via color
   alone is a defect, not a style choice. See `references/color-and-accessibility.md` before
   picking a single color for anything state-bearing (health, cooldown-ready, hostile/friendly).
4. **What's the affordance and feedback loop?** If it's interactive, what signifier makes that
   obvious before the first click, and what confirms the action landed? See
   `references/hierarchy-and-readability.md`.

## Reference files (load the ones relevant to the task)

- `references/hierarchy-and-readability.md` — visual hierarchy, urgency tiers, diegetic/non-diegetic/
  spatial/meta framework, affordance & feedback principles, peripheral-vision readability.
- `references/color-and-accessibility.md` — contrast ratios, colorblind-safe technique checklist,
  "never color alone" enforcement list.
- `references/surfaces.md` — per-surface checklists already scoped to encounter's actual screens:
  party frames/unit frames, minimap + fog-of-war-style reveal, ability bar + cooldowns, gear
  paper-doll + inventory grid, tooltips, status effect icons, floating combat text, cast bars,
  Escape/pregame menu navigation.
- `references/unity-ui-toolkit.md` — UXML/USS/C# architecture and performance rules for when this
  actually gets built in UI Toolkit (BEM class convention, selector cost, element pooling, inline
  style avoidance).
- `references/sources.md` — every source this skill's guidance was drawn from, so a claim can be
  traced back rather than taken on faith.

## How to apply this during a task

- **Critiquing existing/placeholder UI**: run the four-question method against what's there, cite
  which checklist item it fails, propose the smallest fix — not a rebuild.
- **Designing something new**: run the four-question method first, then pull the matching
  `surfaces.md` checklist, then `color-and-accessibility.md` for anything state-bearing.
- **Implementing in UI Toolkit**: `unity-ui-toolkit.md` for structure/perf, but the visual/UX
  decisions still come from the other three files first — don't let implementation convenience
  drive a UX call.
- Always ground recommendations in what this project's screens actually are (see CLAUDE.md's UI
  section) — a generic "games usually do X" isn't a reason on its own for *this* game to do X.
