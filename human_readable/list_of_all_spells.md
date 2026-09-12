# All spells

Every ability currently in `Assets/Resources/Data/Abilities/`, grouped by
role. All spells also share a **global cooldown**: starting any cast
locks out starting a different one for 1.5s, on top of the numbers below.
Regenerate/update this by hand whenever spells are added, renamed, or
retuned — it's a snapshot, not auto-generated.

## Damage

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Firebolt | 120 | 0s | 2s | 50 | 175 damage, applies Burning (10 dmg/sec for 5s) + fire ground patches |
| Icebolt | 120 | 0s | 2s | 50 | 140 damage, applies Slowed (−66% run speed, 5s on direct hit) + ice ground patches |

## Warrior / melee (all require a weapon except Trample and Team Up)

| Spell | Requires weapon | Mana | Cooldown | Cast | Effect |
|---|---|---|---|---|---|
| Reaper's Wheel | Yes | 50 | 8s | Instant | Weapon damage to every enemy in an 8-unit circle around you, + Bleed (10 dmg/sec, 5s) |
| Cleave | Yes | 15 | 2s | Instant | Weapon damage to every enemy in a 120° cone in front of you (8-unit reach) |
| Trample | No | 75 | 30s | Instant | Charge forward 10 units: 30 damage + 3s Stun to everything near the path |
| Seismic Slam | Yes | 75 | 30s | 1s | 10% weapon damage + 3s Stun to every enemy in an 8-unit circle around you |
| Crippling Blow | Yes | 15 | 0s | Instant | 25% weapon damage + −50% run speed on the target for 10s |
| Team Up | No | 150 | 30s | Instant | Charge to an ally (25 range) and redirect all damage *they* take to you instead, for 3s |

## Healing / support

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Radiant Embrace | 150 | 0s | 1.5s | 30 | Heals target for 350 |
| Blessing of Vitality | 200 | 8s | 2s | 30 | Heals target for 350, + Vitality Ward (+10 Armor, 16s) |
| Everliving Touch | 250 | 0s | Instant | 30 | Heal over time: 100 every 3s for 18s (600 total) — stacks if 2 different healers cast it on the same target |
| Aegis of Arcane | 175 | 18s | Instant | 30 | Shields target for 350 (absorbs damage before health, replaces any existing shield) |
| Seraph's Grace | 450 | 0s | 3s | self (50-unit radius) | Heals everyone within 50 units of you, yourself included, for 350 |
| Cleanse | 150 | 6s | Instant | 30 | Removes one active negative effect from the target |
| One For All | 50 | 5s | Instant | 30 | 10% of all damage the target takes is redirected to you instead, for 30 minutes |

## Utility / mobility

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Recall | 200 | 30s | Instant | 40 | Teleports the target instantly to your position |
| Force Compression | 240 | 25s | Instant | 30 (cast range) | Pulls everyone within a 15-unit radius of the target point toward its center |
| Force Expansion | 240 | 25s | Instant | 30 (cast range) | Blasts everyone within a 15-unit radius of the target point outward, away from its center |
| Earthen Bastion | 350 | 10s | Instant | 30 (cast range) | Raises a 25-unit-wide impassable wall at the target point, facing across your current direction. No damage. Lasts until you cast it again (replaces the old wall) — **not yet playable, needs a wall prefab built in the Editor, see review_with_fable.md** |
| Arctic Winds | 300 | 30s | Instant | 30 | Creates a 25-unit-wide dome centered on the target that follows them for 8s, slowing everyone inside (except you, the caster) by 10%, refreshed continuously while they stay in it — **not yet playable, needs a zone prefab built in the Editor, see review_with_fable.md** |

## Auras (cast once, permanently active, gear-less)

Casting any one replaces whichever aura you had active before — only one
can be running at a time.

| Spell | Mana | Cooldown | Cast | Effect |
|---|---|---|---|---|
| Aura of Replenishment | 50 | 0s | Instant | You and allies within 40 units gain +3 mana/sec (+15 per 5s tick) |
| Aura of Regeneration | 50 | 0s | Instant | You and allies within 40 units gain a heal-over-time (+10 hp per 5s tick) |
| Echolocation | 50 | 0s | Instant | Reveals every mob on your minimap, permanently |
