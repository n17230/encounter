# All spells

Every ability currently in `Assets/Resources/Data/Abilities/`, grouped by
role. All spells also share a **global cooldown**: starting any cast
locks out starting a different one for 1.5s, on top of the numbers below.
Regenerate/update this by hand whenever spells are added, renamed, or
retuned — it's a snapshot, not auto-generated.

## Damage

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Firebolt | 60 | 0s | 2s | 50 | 175 damage, applies Burning (10 dmg/sec for 5s) + fire ground patches |
| Icebolt | 60 | 0s | 2s | 50 | 140 damage, applies Slowed (−66% run speed, 5s on direct hit) + ice ground patches |
| Soul Siphon | 125 | 3s | Instant | 30 | 50 damage every 3s for 18s, healing you for 20% of each tick's damage |

## Warrior / melee

| Spell | Requires weapon | Mana | Cooldown | Cast | Effect |
|---|---|---|---|---|---|
| Reaper's Wheel | Yes | 25 | 8s | Instant | Weapon damage to every enemy in an 8-unit circle around you, + Bleed (10 dmg/sec, 5s) |
| Cleave | Yes | 8 | 2s | Instant | Weapon damage to every enemy in a 120° cone in front of you (8-unit reach) |
| Trample | No | 38 | 30s | Instant | Charge forward 10 units: 30 damage + 3s Stun to everything near the path |
| Seismic Slam | Yes | 38 | 30s | 1s | 10% weapon damage + 3s Stun to every enemy in an 8-unit circle around you |
| Crippling Blow | Yes | 8 | 0s | Instant | 25% weapon damage + −50% run speed on the target for 10s |
| Team Up | No | 75 | 30s | Instant | Charge to an ally (25 range) and redirect all damage *they* take to you instead, for 3s |

## Healing / support

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Radiant Embrace | 75 | 0s | 1.5s | 30 | Heals target for 438 |
| Blessing of Vitality | 100 | 8s | 2s | 30 | Heals target for 438, + Vitality Ward (+10 Armor, 16s) |
| Everliving Touch | 125 | 0s | Instant | 30 | Heal over time: 125 every 3s for 18s |
| Aegis of Arcane | 88 | 18s | Instant | 30 | Shields target for 350 |
| Seraph's Grace | 225 | 0s | 3s | self (50-unit radius) | Heals everyone within 50 units of you, yourself included, for 438 |
| Cleanse | 50 | 6s | Instant | 30 | Removes one active negative effect from the target |

## Buffs

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| One For All | 25 | 5s | Instant | 30 | 10% of all damage the target takes is redirected to you instead, for 30 minutes |

## Defense

| Spell | Mana | Cooldown | Cast | Effect |
|---|---|---|---|---|
| Aegis of the Ancient [requires a shield] | 50 | 60s | Instant | For 12s, increases your chance to block incoming physical attacks by 25 percentage points — a blocked hit deals 0 damage |

## Utility / mobility

| Spell | Mana | Cooldown | Cast | Range | Effect |
|---|---|---|---|---|---|
| Recall | 100 | 30s | Instant | 40 | Teleports the target instantly to your position |
| Force Compression | 120 | 25s | Instant | 30 | Pulls everyone within a 15-unit radius of the target point toward its center |
| Force Expansion | 120 | 25s | Instant | 30 | Blasts everyone within a 15-unit radius of the target point outward |
| Earthen Bastion | 175 | 10s | Instant | 30 | Raises a 25-unit-wide impassable wall at the target point. No damage. Lasts until you cast it again. **Not yet playable, see review_with_fable.md** |
| Arctic Winds | 150 | 30s | Instant | 30 | Creates a 25-unit-wide dome that follows the target for 8s, slowing everyone inside by 10% (not you, the caster). **Not yet playable, see review_with_fable.md** |

## Auras (no cast, no keybind - always active for as long as it's in your kit; you can slot more than one at once)

| Spell | Effect |
|---|---|
| Aura of Replenishment | You and allies within 40 units gain +3 mana per 5s |
| Aura of Regeneration | You and allies within 40 units gain a heal-over-time (+10 hp per 5s) |
| Echolocation | Reveals every mob on your minimap |
