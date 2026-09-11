# Things to review in the Editor

Everything below was written and compile-checked outside the Editor (via
`Tools/CompileCheck.csproj`, since the Editor usually holds the project
lock) but never actually run — I have no way to launch Unity or play the
game from here. This is the punch list of what to playtest, roughly in
priority order. Delete items as they're confirmed working; delete the
whole file once it's empty.

## 1. Player 2 falling through the map on spawn — fix applied, needs confirming

- This was diagnosed from reading the code, not from being able to
  reproduce it — the movement validator had no grace period after spawn,
  so a remote (non-host) player's position could get judged before their
  own terrain-snap had round-tripped back to the server, and "corrected"
  right back into the ground. Fixed in `PlayerMovement`
  (`spawnGraceSeconds`) and `PlayerRespawn` (death-respawn now pre-arms
  the validator via `ServerTeleportTo` instead of its own RPC).
- **Please confirm**: connect as a second (non-host) client and watch
  them spawn — do they land and stand normally? Die and respawn a few
  times too, since that path changed as well.
- If it still happens, check the console for `[PlayerMovement] client
  ... movement rejected (BelowGround)` right around spawn/respawn time —
  that would mean the fix didn't fully close the window (e.g.
  `spawnGraceSeconds` isn't long enough over real network latency).

## 2. Mana-on-successful-cast, and mob dots always red

- Both were straightforward, targeted changes — lower risk than the rest
  of this list, but still never actually run.
- Cast Firebolt/Icebolt (2s cast time) and watch your mana bar — it
  should stay full until the cast *completes*, then drop, not drop the
  instant you start casting. Let a cast fizzle on purpose (e.g. target
  walks out of range mid-cast) and confirm no mana was spent.
- Minimap: with Echolocator equipped and a mob currently targeted, its
  pulsed dot should stay plain red like every other mob, never turning
  yellow the way it used to.

## 3. Force Compression / Force Expansion / ground-targeted casting (highest risk — newest, most moving parts)

- Press either spell's hotkey: does a ring reticle appear and follow the
  mouse across the terrain? Does it correctly ignore player/mob
  colliders (aim "through" a mob standing in front of the spot) and only
  hit ground?
- Left-click within range: does it cast there? Outside range: does it
  show "Out of range" and stay in aiming mode rather than canceling?
- Press the same hotkey again while aiming: does it cancel cleanly (no
  reticle left behind)?
- Cast **Force Compression** near a mix of players and mobs standing
  within the 30-unit circle — do all of them get pulled toward the
  center, including the caster if they're standing in the circle too?
- Cast **Force Expansion** the same way — does everyone in the circle
  get blasted radially *outward*, each in their own correct direction
  away from the center (not all flung the same direction)? Do they stop
  a bit past the edge of the circle rather than flying forever?
- **Watch the console during either spell for `[PlayerMovement] client
  ... movement rejected` warnings.** If any appear during/right after a
  cast, the movement-validator suppression isn't covering the full
  window and a moved remote player will get snapped back mid-flight —
  this is the part most likely to be subtly wrong since it couldn't be
  tested at all.
- Does an affected player regain normal WASD control immediately once
  the forced movement ends (not stuck, not a weird pause)?
- Placeholder numbers that were never specified, only guessed — the
  same two on both spells since they're otherwise identical: Range 30,
  Force speed 15/s. The 30-unit diameter (→ 15-unit radius), instant
  cast (0s), 25s cooldown, and 240 mana cost all came from you.

## 4. Recall

- Target a player or a mob and cast Recall — does it teleport instantly
  to right where you're standing?
- **Watch the console for `[PlayerMovement] client ... movement
  rejected` right after a Recall on a remote player.** If it appears,
  the validator re-baseline in `ServerTeleportTo` isn't landing before
  the client's next replicated state does, and the recalled player will
  get snapped back to where they were. Same class of risk as the pull
  spells, never actually run.
- Since characters pass through each other now, teleporting the target
  onto your exact position shouldn't cause any visible clipping/pushing
  — worth a glance to confirm.
- Cooldown (30s), mana cost (200), and range (40) came from you;
  instant cast and no threat generated are still unlabeled guesses.

## 5. Auto-attack

- Right-click a mob (a quick click, not a drag): does it target and arm
  auto-attack without also turning the camera?
- Does the swing only start once actually within melee range, and does
  walking up to a distant target trigger it correctly on arrival?
- Tab to a different target while auto-attacking — does it follow, or
  get stuck attacking the old target?

## 6. Party frames + F1–F5 targeting + minimap compass

- With 2+ clients connected: does each player see the *other* player(s)'
  health/mana bars top-right, correctly updating live as they take
  damage/cast spells?
- Do all clients agree on each player's label (e.g. does everyone see
  the same person labeled "Player 3", regardless of whose screen it's
  on)? This should hold automatically since labels come from the
  canonical `OwnerClientId` sort, not the viewer-filtered row position —
  worth eyeballing with 3+ players specifically because those two only
  diverge once someone in the middle of the order gets skipped.
- Confirm nobody ever sees a frame for themselves.
- With only 1 player connected, confirm nothing draws (no empty box).
- **F1–F5**: with 3+ players connected, confirm each key targets
  whoever's actually drawn in that row on *your own* screen — per the
  design this can be a different canonical player than the key number
  suggests once you've skipped yourself (e.g. your F2 might target
  "Player 3"). This is the part most likely to be subtly wrong since it
  was never run.
- **Known, deliberately unresolved conflict**: F1–F5 are also selectable
  ability hotkeys in the Options/loadout menu. If you'd bound an ability
  to F2, pressing it now does both — fires the ability *and* retargets.
  Worth deciding whether to remove F1–F5 from the ability-keybind pool,
  or leave it.
- **Compass**: do N/E/S/W actually line up with real N/E/S/W (i.e. does
  walking toward the "N" label increase world Z)? The math should be
  right but was never seen rendered.

## 7. Minimap / Echolocator

- Confirm the map is blank with no items equipped (just your own dot).
- Equip Echolocator: do mobs pulse onto the map every ~5s, stay frozen
  (not tracking), and fade out over ~4s before the next pulse (leaving
  a ~1s gap with nothing)?
- Transmitting Beacon on another player: do they show as a green dot on
  your map even with no reveal gear of your own equipped?

## 8. Gear — ring slots and the recent renumbering

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

## 9. General combat/economy numbers worth a sanity pass

- Mana: 120 per bolt, 1000 pool (1250 with Staff's new +250 max mana),
  regen in 5s ticks — does an actual fight feel like mana is a real
  constraint now, or too tight/loose?
- Burning: 10 dmg/s for 5s — feels right, or needs another pass?
- Amulet of Replenishment (+15 mana per 5s tick, renamed from Amulet of
  Mana) and Amulet of Regeneration (+10 hp per 5s tick, renamed from
  Amulet of Rejuvenation) — relative strength of the two next to each
  other, and whether the new names read clearly against their effect
  names ("Regeneration" grants the "Rejuvenation" buff — the underlying
  effect wasn't renamed, only the item).

## 10. Reported by the user, not yet looked into

- Firebolt/Icebolt's VFX does not home in on the target — it flies
  straight rather than tracking. The crude tracer (`Projectile`'s actual
  movement, which the VFX is presumably meant to follow/represent) does
  home in correctly. So the underlying homing logic works; something in
  how the visual effect is attached to or driven by the projectile is
  the mismatch. Not investigated yet.

## 11. Still outstanding from earlier sessions (unrelated to the above, just parked here)

- The VPS still runs the **pre-refactor server build** — its network
  protocol no longer matches this client at all. Nothing will connect
  until a fresh Linux Dedicated Server build is deployed.
- NavMesh mob pathfinding (mobs currently walk straight at you and can
  get stuck on walls) — not started, see `CLAUDE.md` "Not yet done".
