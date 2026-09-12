# All items

Every item currently in `Assets/Resources/Data/Items/`, sorted by gear
slot (in `GearSlot` enum order). Regenerate/update this by hand whenever
items are added, renamed, or retuned — it's a snapshot, not auto-generated.

## Necklace

| Item | Effect |
|---|---|
| Amulet of Vitality | +150 Max Health, +5 Armor |
| Amulet of the Magi | +100 Max Mana, +0.4 Mana Regen/sec (+2 per 5s tick) |
| Amulet of the Berserker | +5 Weapon Damage (basic attacks and weapon-scaling abilities) |

## Chest

| Item | Effect |
|---|---|
| Barbarian's Mantle | Above 50% HP: −15% damage taken. Below 50% HP: +15% damage dealt. |

## Boots

| Item | Effect |
|---|---|
| Armored Boots | +5 Armor |
| Boots of Lightness | Press Jump again while airborne to hover in place for 2s (WASD still steers) — once per airtime |
| Ice Cleats | Immune to Slow from ground patches (a direct hit can still slow you) |
| Swift Boots | +3% Run Speed |

## Ring 1 / Ring 2

(Either physical ring slot — an item just needs the `Ring1` category to go in either.)

| Item | Effect |
|---|---|
| Transmitting Beacon | You always show on allies' minimaps, regardless of their own reveal gear |

## Trinket

| Item | Effect |
|---|---|
| Ember Stone | +10% damage dealt; wearer is permanently Burning (self-inflicted, no threat) |
| The Everflow | +1 Mana Regen/sec (+5 per 5s tick) |

## Main Hand

| Item | Weapon | Effect |
|---|---|---|
| Broad Sword | 40 damage / 2s swing, 2 range | +40% threat generated, +100 Max Health |
| Staff | 20 damage / 2s swing, 2 range | −10% mana cost, +250 Max Mana. Two-handed — occupies Off Hand too |
| 2H Axe | 80 damage / 3s swing, 2 range | Two-handed — occupies Off Hand too, no other bonus |
| Holy Scepter | none (caster stat-stick) | +10% healing done |
| Hunter's Bow | 60 damage / 2.5s swing, **50 range** | No other bonus |

## Off Hand

| Item | Effect |
|---|---|
| Aegis of the Unstoppable | +20 Armor. Counts as a shield (Aegis of the Ancient requires this) |
| Aegis of Reflection | +10 Armor, reflects 3% of incoming damage back at the attacker. Counts as a shield |
| Tomb of the Magi | +50 Max Mana, +0.4 Mana Regen/sec (+2 per 5s tick) |

## Slots with nothing yet

Helmet, Cape, Gloves, Belt, Legs.
