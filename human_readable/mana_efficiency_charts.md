# Mana efficiency and weapon charts

Base values from the ability, effect, weapon and item assets, before
damage/healing multipliers and armor. "Extra effect" is TRUE when the spell
or weapon does something beyond the headline number; the effect column is
blank when it is FALSE.

**DPS / HPS** = damage (or healing) divided by cast time (spells) or swing
interval (weapons). Cooldown is not part of it; it has its own CD column. A
dash means there is no cast time, cooldown or mana cost to use.

## Damage per mana

| Spell | Damage / mana | DPS | CD | Extra effect | Effect |
|---|---|---|---|---|---|
| Firebolt | 2.92 (175 / 60) | 87.5 (175 / 2s) | - | TRUE | Leaves 5 fire patches (burn 10/s, up to 5s). |
| Soul Siphon | 2.40 (300 / 125) | 16.7 (300 / 18s) | 3s | TRUE | 50 per 3s tick x 6 over 18s, so the per-mana number is total. Recasting while it is up only extends it, it does not stack. Heals you for 20% of each tick (60 total). |
| Icebolt | 2.33 (140 / 60) | 70.0 (140 / 2s) | - | TRUE | Direct hit slows 66% for 5s. Leaves 5 ice patches (slow 3s). |
| Trample | 0.79 (30 / 38) | - | 30s | TRUE | Charges 10 forward and hits everything within 2.5 of the path. Stuns 3s. |

### Weapon-scaling melee

Damage depends on the equipped weapon, so these are fractions of weapon
damage (W). With a 100-damage weapon, multiply by 100.

| Spell | x W / mana | x W / sec | CD | Extra effect | Effect |
|---|---|---|---|---|---|
| Cleave | 0.125 (1.0 / 8) | - | 2s | TRUE | 120 degree cone, radius 8, hits everyone. |
| Crippling Blow | 0.031 (0.25 / 8) | - | - | TRUE | Slows the target 50% for 10s. |
| Reaper's Wheel | 0.04 (1.0 / 25) | - | 8s | TRUE | Circle radius 8, hits everyone. Adds Bleed (10/s for 8s, +80). |
| Seismic Slam | 0.004 (0.1 / 25) | 0.1 (0.1 / 1s) | 30s | TRUE | Circle radius 8. Stuns 3s. |

## Healing per mana

| Spell | Healing / mana | HPS | CD | Extra effect | Effect |
|---|---|---|---|---|---|
| Everliving Touch | 6.00 (750 / 125) | 41.7 (750 / 18s) | - | TRUE | Heal over time only: 125 per 3s x 6 over 18s, no instant heal. Recasting on the same target only extends it. |
| Radiant Embrace | 4.75 (356.25 / 75) | 237.5 (356.25 / 1.5s) | - | FALSE | |
| Aegis of Arcane (absorb shield, not a heal) | 5.00 (440 / 88) | - | 18s | FALSE | |
| Blessing of Vitality | 4.38 (438 / 100) | 219 (438 / 2s) | 8s | TRUE | +10 Armor for 16s. |
| Seraph's Grace | 2.50 per target (438 / 175) | 146 per target (438 / 3s) | - | TRUE | Heals everyone within 50 of the caster, including mobs and the caster. |
| Holy Scepter (weapon attack) | - | 16.7 (25 / 1.5s) | - | TRUE | Basic attack heals 25 per swing at range 40. The item also gives +10% healing done. |

## Weapon damage

Player weapons only. DPS = damage per swing / swing interval, before armor
and damage multipliers. Auto-attacks cost no mana and have no cooldown.

| Weapon | Damage / swing | Swing interval | DPS | Range | Extra effect | Effect |
|---|---|---|---|---|---|---|
| Broad Sword | 90 | 1.5s | 60.0 | 2 | TRUE | Item gives +40% threat generated and +100 max health. |
| Armorbreaker | 100 | 1.5s | 66.7 | 2 | TRUE | Each hit applies Sundered Armor: -2 Armor per stack, up to 5 stacks (-10), 10s on a shared timer. |
| 2H Axe | 140 | 2.5s | 56.0 | 2 | FALSE | |
| Hunter's Bow | 100 | 2s | 50.0 | 50 | FALSE | |
| Staff of the Magi | 75 | 2s | 37.5 | 40 | TRUE | Item gives +250 max mana and +1 mana regen per second. |
| Fists (unarmed fallback) | 15 | 1.5s | 10.0 | 2 | FALSE | |
| Holy Scepter | 0 | 1.5s | - | 40 | TRUE | Heals instead of damaging (25 per swing, see the healing chart). The item also gives +10% healing done. |

2H Axe, Hunter's Bow and Staff of the Magi are two-handed (they take the
off-hand slot).

## Left out

- **Auras:** they cost 0 mana, so there is no per-mana figure. Aura:
  Regeneration heals 10 per 5s tick, and Aura: Replenish adds mana regen.
- **No damage or healing of their own:** Cleanse, One For All, Team Up,
  Recall, Force Compression/Expansion, Summon Wall, Arctic Winds, Aegis
  of the Ancient, Echolocation and Perception. Resurrect sets a dead player to 25%
  health and 25% mana rather than healing an amount, so it has no
  per-mana figure either.
- **Patch and bleed damage:** Firebolt's burn patches are not counted in
  its number. Burn deals 10/s and a patch lasts 6s.
- **Mob weapons:** Goblin Claws, Ogre Club, the skeleton weapons and the
  Tactician's weapons are not in the weapon chart.
