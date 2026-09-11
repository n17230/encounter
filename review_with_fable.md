# Things to review in the Editor

Everything below was written and compile-checked outside the Editor (via
`Tools/CompileCheck.csproj`, since the Editor usually holds the project
lock) but never actually run — I have no way to launch Unity or play the
game from here. This is the punch list of what to playtest, roughly in
priority order. Delete items as they're confirmed working; delete the
whole file once it's empty.

## 1. Vacuum / ground-targeted casting (highest risk — newest, most moving parts)

- Press Vacuum's hotkey: does a ring reticle appear and follow the mouse
  across the terrain? Does it correctly ignore player/mob colliders (aim
  "through" a mob standing in front of the spot) and only hit ground?
- Left-click within range: does it cast there? Outside range: does it
  show "Out of range" and stay in aiming mode rather than canceling?
- Press the same hotkey again while aiming: does it cancel cleanly (no
  reticle left behind)?
- Cast it near a mix of players and mobs standing within the 30-unit
  circle — do all of them get pulled toward the center, including the
  caster if they're standing in the circle too?
- **Watch the console during a pull for `[PlayerMovement] client ...
  movement rejected` warnings.** If any appear during/right after a
  pull, the movement-validator suppression isn't covering the full
  window and a pulled remote player will get snapped back mid-pull —
  this is the part most likely to be subtly wrong since it couldn't be
  tested at all.
- Does a pulled player regain normal WASD control immediately once they
  reach the center (not stuck, not a weird pause)?
- Placeholder numbers on `AbilityVacuum` that were never specified,
  only guessed — reconsider all of these: Range 30, Cooldown 15s, Mana
  cost 120, Pull speed 15/s. Only the 30-unit diameter (→ 15-unit
  radius) came from you, and cast time is now 0 (instant) per request.

## 2. Auto-attack

- Right-click a mob (a quick click, not a drag): does it target and arm
  auto-attack without also turning the camera?
- Does the swing only start once actually within melee range, and does
  walking up to a distant target trigger it correctly on arrival?
- Tab to a different target while auto-attacking — does it follow, or
  get stuck attacking the old target?

## 3. Minimap / Echolocator

- Confirm the map is blank with no items equipped (just your own dot).
- Equip Echolocator: do mobs pulse onto the map every ~5s, stay frozen
  (not tracking), and fade out over ~4s before the next pulse (leaving
  a ~1s gap with nothing)?
- Transmitting Beacon on another player: do they show as a green dot on
  your map even with no reveal gear of your own equipped?

## 4. Gear — ring slots and the recent renumbering

- Equip a ring (Transmitting Beacon): does it go into Ring 1, and if
  Ring 1 is already full, does a second ring correctly fall into Ring 2
  instead of failing?
- Check Main Hand / Off Hand / Trinket items (Broad Sword, Staff,
  Shield, Fire Trinket) still equip into the right slot — their slot
  index shifted when Ring3/Ring4 were removed and I renumbered them by
  hand.
- If you had gear equipped in a saved profile from before that change,
  expect Main/Off/Trinket to have reset to empty — known, not a bug,
  just re-equip once.

## 5. General combat/economy numbers worth a sanity pass

- Mana: 120 per bolt, 1000 pool, regen in 5s ticks — does an actual
  fight feel like mana is a real constraint now, or too tight/loose?
- Burning: 10 dmg/s for 5s — feels right, or needs another pass?
- Amulet of Mana (+15 per 5s tick) and Amulet of Rejuvenation (+10 hp
  per 5s) — relative strength of the two next to each other.

## 6. Reported by the user, not yet looked into

- Firebolt/Icebolt's VFX does not home in on the target — it flies
  straight rather than tracking. The crude tracer (`Projectile`'s actual
  movement, which the VFX is presumably meant to follow/represent) does
  home in correctly. So the underlying homing logic works; something in
  how the visual effect is attached to or driven by the projectile is
  the mismatch. Not investigated yet.

## 7. Still outstanding from earlier sessions (unrelated to the above, just parked here)

- The VPS still runs the **pre-refactor server build** — its network
  protocol no longer matches this client at all. Nothing will connect
  until a fresh Linux Dedicated Server build is deployed.
- NavMesh mob pathfinding (mobs currently walk straight at you and can
  get stuck on walls) — not started, see `CLAUDE.md` "Not yet done".
