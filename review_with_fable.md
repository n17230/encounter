# Things to review in the Editor

Everything below was written and compile-checked outside the Editor (via
`Tools/CompileCheck.csproj`, since the Editor usually holds the project
lock) but never actually run — I have no way to launch Unity or play the
game from here. This is the punch list of what to playtest, roughly in
priority order. Delete items as they're confirmed working; delete the
whole file once it's empty.

## 0. Crippling Blow / Seismic Slam weapon-scaled damage, Trample retune, Tomb of the Magi, The Everflow

- **`AbilityData.WeaponDamagePercent`** replaced the old all-or-nothing
  `UseWeaponDamage` bool (a straight rename/generalization, same field
  slot in the YAML - existing Reaper's Wheel/Cleave behavior is
  unchanged, still 100% weapon damage). New: **Crippling Blow** now also
  deals 25% weapon damage (previously 0, slow-only) and **Seismic Slam**
  now also deals 10% weapon damage (previously 0, stun-only) - both on
  top of their existing effects. Confirm both actually show nonzero
  damage numbers now, scaling with whatever's in your Main Hand (Fists
  excluded - both still require a real melee weapon to cast at all).
- **Trample retuned**: damage 75 → 30, cooldown 15s → 30s (a
  significant nerf - confirm it still feels worth using at the new
  numbers).
- **Tomb of the Magi** (Off Hand, +50 max mana, +0.4 mana/sec = +2 per
  5s tick) and **The Everflow** (Trinket, +1 mana/sec = +5 per 5s tick) -
  both plain stat items, lowest risk, never equipped/tested.

## 1. Global cooldown, Aura spells (replacing the amulets + Echolocator), Cleanse, 2 new amulets

- **Global cooldown (explicitly requested)**: casting ANY ability now
  locks out starting a different one for 1.5s (not specified, the
  standard MMO GCD length - flagged placeholder), independent of that
  ability's own cooldown. **Please confirm**: cast one spell, and
  immediately try a completely different one on a different key within
  ~1.5s - it should show "Global cooldown" and do nothing, then work
  normally once the GCD passes. Also confirm it does NOT block re-use of
  the SAME ability once ITS OWN (possibly longer) cooldown is up - the
  GCD should never be the binding constraint for a slow ability, only
  for chaining two different fast/instant ones back to back. Never run.
- **Aura spells (new mechanic, replacing 3 gear items)**: the two
  amulets and Echolocator no longer exist as items - equipping/gear
  pickers should show them gone entirely. In their place: **Aura of
  Replenishment**, **Aura of Regeneration**, **Echolocation** - cast any
  one (instant, 50 mana, 0 cd - all placeholders, not specified), and its
  effect (mana pulse / heal pulse / mob-reveal) should turn on
  permanently, with **no gear required and no expiry**. Confirm the
  pulse still only reaches allies within 40 units (same as the old
  amulets) and includes yourself. **Confirm exclusivity**: cast Aura of
  Replenishment, then cast Aura of Regeneration - Replenishment's mana
  pulse should stop the instant Regeneration's heal pulse starts (only
  one active at a time, per your explicit answer to a clarifying
  question - not a guess). Also confirm Echolocation slotted in as one
  of the three behaves the same way (casting it turns off whichever
  aura was previously active, and vice versa). None of this has ever
  been run - it's a genuinely new mechanic (permanent, gear-less,
  spell-granted auras) with no precedent to lean on.
- **Cleanse** (new mechanic): instant, 150 mana, removes one active
  negative effect from the target (e.g. Burning, Slowed, Bleeding,
  Stunned, Crippled) - never removes a buff/aura. If the target has more
  than one debuff active, whichever gets picked is arbitrary (not
  specified which should take priority) - confirm it at least always
  removes something when a debuff is present, and does nothing/fizzles
  usefully when there isn't one. Range 30 and 6s cooldown are guesses.
- **Amulet of Vitality** (+150 max health) and **Amulet of the Magi**
  (+100 max mana, +0.4 mana/sec = +2 per 5s tick) - both plain
  stat-bonus Necklace items, lowest risk in this batch, but still never
  equipped/tested.

## 2. Broad Sword threat, 2H Axe, two-handed/off-hand exclusivity

- **Broad Sword**: threat-generation bonus changed from +20% to +40%
  (`ThreatMultiplier`) - straightforward number change, low risk.
- **2H Axe** (new item, `two_handed_axe`, MainHand, `TwoHanded: true`,
  80 weapon damage, 3s swing interval - set by the user 2026-09-12, no
  longer a guess). No other bonuses were requested, so it has none (no
  threat/HP/armor bonus like Broad Sword has) - confirm that's intended,
  not an oversight.
- **Two-handed/off-hand mutual exclusivity** (new mechanic,
  `ItemData.TwoHanded`): equipping the 2H Axe into MainHand auto-clears
  whatever's in OffHand (e.g. unequips Shield), and equipping something
  into OffHand while the 2H Axe is equipped auto-clears the 2H Axe from
  MainHand instead - handled both in the gear menu (`MainMenu.cs`, for
  instant visual feedback) and authoritatively in
  `CharacterEquipment.SetGearServerRpc` (a hard backstop - MainHand is
  always processed before OffHand in the slot loop, so OffHand is what
  gets silently rejected server-side if the two ever conflict, no matter
  what the client sent). **Please confirm**: equip 2H Axe → Shield slot
  visibly empties; equip Shield with 2H Axe on → 2H Axe slot visibly
  empties (and your auto-attack should fall back toward Fists/whatever
  else is in MainHand, not silently keep swinging for 80). Also confirm
  a saved profile that somehow has both isn't possible to reach through
  the menu at all.

## 3. Seven new warrior spells/item (Reaper's Wheel, Trample, Cleave, Team
   Up, Crippling Blow, Seismic Slam, Barbarian's Mantle) — biggest and
   most novel batch yet, several brand-new mechanics

This batch introduced multiple mechanics that never existed before, so
treat all of it as unverified:

- **Stun (new mechanic)**: `StatusEffectData.IsStun`, checked via
  `CharacterStats.IsStunned`. Currently only `EnemyAI` obeys it (mobs
  hit by Trample/Seismic Slam freeze in place, no movement/attacks, for
  the effect's duration). **Players are NOT gated by stun** - nothing in
  this batch stuns a player, so `PlayerMovement`/`PlayerAbilities` were
  deliberately left unchanged. If a future ability stuns a player, this
  will need adding there too.
- **"Enemies" = anything without a `PlayerMovement` component** (mirrors
  the existing "allies" = has `PlayerMovement` filter `AreaAroundCaster`
  already used, just inverted). Reaper's Wheel/Seismic Slam
  (`EnemiesAroundCaster`) and Cleave (`ConeAroundCaster`) all use this.
  Confirm they hit mobs but never other players.
- **Assumed AoE radius: 8 units** for Reaper's Wheel, Cleave, and Seismic
  Slam's "around you" reach - not specified in the request; still just a
  placeholder (`AbilityData.GroundEffectRadius`) - trivial to retune.
  Cleave's cone angle was set by the user directly: **120°** (matches
  the facing cone other abilities already use).
- **"[require melee weapon]" was interpreted as a hard cast-blocking
  gate**: `AbilityData.RequiresMeleeWeapon` fizzles/rejects the cast if
  `CharacterEquipment.MainHandWeapon` is null (fists don't count),
  independent of whether the ability also scales off weapon damage.
  Applied to Reaper's Wheel, Cleave, Crippling Blow, Seismic Slam (all 4
  tagged abilities) - NOT to Trample or Team Up (untagged). Confirm
  casting any of the 4 unarmed shows "Requires a melee weapon" and does
  nothing.
- **"Weapon damage" scaling** (Reaper's Wheel, Cleave): reads whatever
  the caster's auto-attack would currently swing with
  (`PlayerAutoAttack.ResolvedWeapon` - equipped main hand, or Fists) at
  the moment the ability resolves. Confirm swapping weapons changes the
  ability's damage accordingly.
- **Trample (new self-charge mechanic)**: caster dashes forward 10 units
  in whatever direction they're currently facing (no target needed),
  hitting/stunning any enemy within 2.5 units of that straight-line path
  (a one-time resolve-time check, not continuous collision - an enemy
  that darts into the path mid-charge won't be caught). Charge speed
  assumed **20 units/sec** (not specified) - reuses the existing
  Vacuum/Force-pull rail (`PlayerMovement.ServerBeginPull`) under the
  hood, so it should feel like getting pulled, just self-initiated.
  Confirm: no target required, moves the caster (not the mobs), damage +
  3s stun lands on everything roughly in the path, and it doesn't let
  you charge through walls/off the map (no obstruction check exists -
  flag if that matters).
- **Team Up (new gap-closer + ally buff)**: interpreted as a *support*
  ability - `RequiresTarget` (an ally, like the healing spells), the
  caster charges to just short of the target (2-unit clearance) using
  the same charge rail as Trample, then the target (not the caster)
  gets "reduced damage taken" for 15s. This needed a brand new stat,
  `StatType.DamageTakenMultiplier` (applied in `CharacterStats.DealDamage`
  right after armor mitigation) - also reused by Barbarian's Mantle
  below. Assumed **cast range 25** (how far away you can initiate the
  charge) - not specified. Confirm: charging to a full-health ally
  actually reduces damage they take by 10% for 15s, and the caster
  visibly moves to them.
- **Crippling Blow**: no new mechanic - same pattern as Icebolt's Slow
  (a `RunSpeed` `PercentAdditive` -50% modifier, 10s), just standalone
  and melee-gated. Lowest-risk item in this batch.
- **Barbarian's Mantle (new HP-threshold-conditional item mechanic)**:
  new `ItemData.HpThresholdEffects` list, evaluated once/sec in
  `CharacterEquipment` (same cadence as auras) - above 50% HP applies
  -15% `DamageTakenMultiplier`, below 50% applies +15% `DamageMultiplier`,
  swapping automatically as health crosses the line (only re-applies
  modifiers on an actual crossing, not every tick). **Assumed slot:
  Chest** ("(armor)" read as body armor, not the `Cape` slot despite the
  "mantle" name) - easy one-line change if you want it in `Cape`
  instead. Confirm: damage taken/dealt actually changes right as you
  cross 50% HP in either direction, and unequipping it cleanly removes
  whichever side was active.
- None of these 7 have `CastVfxPrefab`/`ProjectilePrefab` assigned (no
  visual assets were provided) - they'll cast with no cast-bar VFX,
  matching how earlier spells started out.

## 4. Five new healing spells + shields + Holy Scepter — biggest untested batch yet

- **Radiant Embrace**: cast on a hurt ally, confirm exactly 350 healing
  lands (or 385 with Holy Scepter's +10%).
- **Blessing of Vitality**: confirm the heal lands AND the target gets
  "Vitality Ward" for 16s, AND that while it's up they take noticeably
  less damage (10% less — e.g. a 100-damage hit should land for ~90,
  modulo their own armor). This is implemented as a flat +10 Armor
  buff, reusing the same formula Shield the item already uses — should
  just work, but never seen combined with a target's own existing armor.
- **Aegis of Arcane**: shield an ally for 350, then have them take
  damage — confirm damage is absorbed first (health doesn't drop until
  350 has been eaten), the HUD shows a cyan "Shield: N" number counting
  down, and once it hits 0 further damage reduces health normally.
  Confirm a shielded hit does NOT also get redirected if the shielded
  player also has One For All active on them (shield should apply
  first, so redirect only sees what's left after the shield). Also
  confirm shielding someone who already has a shield **replaces** the
  old amount rather than adding to it (intentional design choice, not
  a bug).
- **Everliving Touch**: 600 total healing over 18s in 100/3s ticks.
  Have two different players cast it on the same target and confirm
  both HoTs tick independently (600 total heal doubles up), same as
  Amulet of Regeneration's aura already should.
- **Seraph's Grace**: stand near 2+ other players, cast it, confirm
  everyone within 50 units (including yourself) heals for 350
  simultaneously, and nobody outside 50 units does. This is a brand
  new "no targeting at all, just hits everyone near you" cast path
  (`AreaAroundCaster`) — never exercised before.
- **Holy Scepter**: equip it and confirm ALL of the above heals (and
  Amulet of Rejuvenation-style ticks) increase by 10% — but confirm the
  Mana/Regen amulets' *aura* healing does NOT get the bonus (deliberate:
  the scepter boosts cast spells, not passive gear auras).
- All 5 abilities' `Range: 30` (where applicable) was never specified —
  only the numbers listed above came from you.

## 5. Player 2 falling through the map on spawn — fix applied, needs confirming

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

## 6. Mana-on-successful-cast, and mob dots always red

- Both were straightforward, targeted changes — lower risk than the rest
  of this list, but still never actually run.
- Cast Firebolt/Icebolt (2s cast time) and watch your mana bar — it
  should stay full until the cast *completes*, then drop, not drop the
  instant you start casting. Let a cast fizzle on purpose (e.g. target
  walks out of range mid-cast) and confirm no mana was spent.
- Minimap: with Echolocator equipped and a mob currently targeted, its
  pulsed dot should stay plain red like every other mob, never turning
  yellow the way it used to.

## 7. Force Compression / Force Expansion / ground-targeted casting (highest risk — newest, most moving parts)

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

## 8. Recall

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

## 9. One For All (damage redirect) — genuinely new mechanic, unrun

- Cast it on an ally, have a mob hit *them*, and confirm 10% of that
  damage lands on *you* (the caster) instead — watch both health bars.
  The other 90% should still hit the ally normally.
- Confirm the redirected 10% doesn't get re-armored on your end (it's a
  direct siphon of the already-mitigated amount, not a fresh "hit"
  against your own armor) — the math to check: if the ally takes X
  damage after their own armor, you should lose exactly 0.1×X, not
  something further reduced by your armor too.
- If the redirect kills *you* (caster) rather than the ally, confirm you
  actually die (OnDeath fires) — this was implemented via a shared
  death-check helper but never seen run.
- Cast it on player A, then recast it on player B (different target) —
  does A's buff actually go away (check their buff/effects display),
  and does B now have it instead? Recast on B again (same current
  holder) — should just refresh/extend, not do anything weird.
- **Now handled deliberately** (was an unhandled edge case, fixed the
  same day): two different casters each casting their own One For All
  on the *same* third player. Caster A bonds the target first; caster B
  then casts it on the same target — B should cleanly take over the
  bond (target's redirect now points to B, not A), not conflict or
  silently corrupt state. This is the new `EffectStackingMode.Override`
  behavior (see CLAUDE.md) — covered by EditMode tests
  (`OverrideModeAlwaysWinsEvenWhenShorterAndReattributes`) but never
  seen running against a real second player.
- Separately, confirm a **heal-over-time actually stacks** when two
  different players each apply one to the same target — e.g. two people
  wearing Amulet of Regeneration standing near the same ally should both
  tick their own heal independently (double healing), not fight over a
  single shared instance. This is `EffectStackingMode.StackPerCaster`
  (`EffectRejuvenation`), also covered by tests
  (`StackPerCasterGivesEachCasterTheirOwnInstance`) but never run live.
  Known cosmetic gap either way: the on-screen buff timer only ever
  shows **one** entry even when two instances are ticking (see CLAUDE.md
  "Known, deliberate limitation").
- Cooldown (5s) and Range (30) are unconfirmed guesses; duration (30
  min), redirect (10%), mana cost (50), and instant cast all came from
  you.

## 10. Auto-attack

- Right-click a mob (a quick click, not a drag): does it target and arm
  auto-attack without also turning the camera?
- Does the swing only start once actually within melee range, and does
  walking up to a distant target trigger it correctly on arrival?
- Tab to a different target while auto-attacking — does it follow, or
  get stuck attacking the old target?

## 11. Party frames + F1–F5 targeting + minimap compass

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

## 12. Minimap / Echolocator

- Confirm the map is blank with no items equipped (just your own dot).
- Equip Echolocator: do mobs pulse onto the map every ~5s, stay frozen
  (not tracking), and fade out over ~4s before the next pulse (leaving
  a ~1s gap with nothing)?
- Transmitting Beacon on another player: do they show as a green dot on
  your map even with no reveal gear of your own equipped?

## 13. Gear — ring slots and the recent renumbering

- Equip a ring (Transmitting Beacon): does it go into Ring 1, and if
  Ring 1 is already full, does a second ring correctly fall into Ring 2
  instead of failing?
- Check Main Hand / Off Hand / Trinket items (Broad Sword, Staff,
  Shield, Ember Stone) still equip into the right slot — their slot
  index shifted when Ring3/Ring4 were removed and I renumbered them by
  hand.
- If you had gear equipped in a saved profile from before that change,
  expect Main/Off/Trinket to have reset to empty — known, not a bug,
  just re-equip once.

## 14. General combat/economy numbers worth a sanity pass

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

## 15. Reported by the user, not yet looked into

- Firebolt/Icebolt's VFX does not home in on the target — it flies
  straight rather than tracking. The crude tracer (`Projectile`'s actual
  movement, which the VFX is presumably meant to follow/represent) does
  home in correctly. So the underlying homing logic works; something in
  how the visual effect is attached to or driven by the projectile is
  the mismatch. Not investigated yet.

## 16. Still outstanding from earlier sessions (unrelated to the above, just parked here)

- The VPS still runs the **pre-refactor server build** — its network
  protocol no longer matches this client at all. Nothing will connect
  until a fresh Linux Dedicated Server build is deployed.
- NavMesh mob pathfinding (mobs currently walk straight at you and can
  get stuck on walls) — not started, see `CLAUDE.md` "Not yet done".
