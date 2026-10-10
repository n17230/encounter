// Pure ability/item tooltip text formatting, extracted out of MainMenu so it
// can be shared by both UI Toolkit panels (SkillsPanelController,
// EquipmentPanelController) that use it via HoverTooltip. No side effects - only
// reads the passed-in data asset.
public static class TooltipText
{
    // The "what this ability does" text - a data-driven stat/effect
    // breakdown (no authored flavor text exists on AbilityData), used as
    // the Skills panel's Available Abilities card description. Format
    // follows the standard MMO convention (WoW spell tooltips): a compact
    // cast-time/cooldown/range block first, then the effect as a plain-
    // language sentence rather than bare "Label: value" lines. Mana cost is
    // NOT included here - it's shown separately, next to the ability's name.
    // Does not include the ability's own name - callers that want a
    // combined heading + body (nothing currently does) can prefix it
    // themselves.
    public static string BuildAbilityBody(AbilityData ability)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        if (ability.IsAuraSpell)
        {
            // "(passive)" next to the name (see SkillsPanelController) already
            // covers "always active once slotted" - no need to repeat it here.
            if (ability.Effect != null) sb.AppendLine(DescribeEffect(ability.Effect, ability.Effect.Duration));
            // Matches CLAUDE.md's documented Minimap behavior and the same
            // wording BuildItemTooltip already uses for this identical
            // mechanic (ItemData.Reveals) - a periodic ping, not a live
            // tracker, so "reveals" alone would overstate it.
            if ((ability.AuraReveals & MinimapReveal.Mobs) != 0) sb.AppendLine("Pulses monsters onto the minimap every 5s.");
            if (ability.AuraShowsPerceptionOrbs) sb.AppendLine("Shows an orb above every mob, indicating its targeting style.");
            return sb.ToString().TrimEnd();
        }

        if (!ability.AreaAroundCaster) sb.AppendLine($"Range: {ability.Range}");
        sb.AppendLine(ability.CastTime > 0f ? $"{ability.CastTime} sec cast" : "Instant");
        if (ability.Cooldown > 0f) sb.AppendLine($"{ability.Cooldown} sec cooldown");
        sb.AppendLine();

        System.Collections.Generic.List<string> effectParts = new System.Collections.Generic.List<string>();
        if (ability.Damage > 0f) effectParts.Add($"deals {ability.Damage} damage");
        if (ability.HealAmount > 0f) effectParts.Add($"heals for {ability.HealAmount}");
        if (ability.ShieldAmount > 0f) effectParts.Add($"shields for {ability.ShieldAmount}");
        if (effectParts.Count > 0)
        {
            string sentence = string.Join(" and ", effectParts);
            sb.AppendLine(char.ToUpperInvariant(sentence[0]) + sentence.Substring(1) + ".");
        }

        if (ability.IsGroundTargeted)
        {
            // A structure's footprint is its wall width, not a circle -
            // GroundEffectRadius is only the (unused) leftover there.
            if (ability.IsPersistentStructure)
            {
                sb.AppendLine($"Raises a {WallSegmentLayout.EffectiveWidth(ability.StructureWidth, SegmentedWall.SegmentWidth)}-wide wall.");
            }
            else
            {
                sb.AppendLine($"Ground-targeted: {ability.GroundEffectRadius * 2f} diameter area.");
            }
            if (ability.ForceSpeed > 0f)
            {
                string direction = ability.PushAway ? "outward, away from" : "inward, toward";
                sb.AppendLine($"Blasts everyone in the area {direction} the center at {ability.ForceSpeed}/s.");
            }
        }
        if (ability.AreaAroundCaster) sb.AppendLine($"Centered on you: {ability.GroundEffectRadius} radius, affects all players.");
        if (ability.RecallTarget) sb.AppendLine("Teleports the target to your location.");
        if (ability.ResurrectTarget) sb.AppendLine($"Resurrects a dead player with {ability.ResurrectHealthPercent * 100f:0}% health and {ability.ResurrectManaPercent * 100f:0}% mana.");

        if (ability.Effect != null)
        {
            float duration = ability.DirectHitEffectDuration > 0f ? ability.DirectHitEffectDuration : ability.Effect.Duration;
            sb.AppendLine(DescribeEffect(ability.Effect, duration));
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeEffect(StatusEffectData effect, float duration)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(effect.DisplayName).Append(':');
        if (effect.TickDamage > 0f) sb.Append($" {effect.TickDamage} dmg/{effect.TickInterval}s");
        if (effect.TickHeal > 0f) sb.Append($" +{effect.TickHeal} hp/{effect.TickInterval}s");
        if (effect.DamageRedirectPercent > 0f) sb.Append($" redirects {effect.DamageRedirectPercent * 100f:0}% of damage taken to the caster");
        foreach (StatBonus bonus in effect.Modifiers) sb.Append(' ').Append(DescribeBonus(bonus));
        sb.Append($" for {duration}s");
        return sb.ToString();
    }

    public static string BuildItemTooltip(ItemData item)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(item.ItemName);
        sb.AppendLine($"Slot: {item.Slot}");
        if (item.Weapon != null)
        {
            sb.AppendLine(item.Weapon.HealAmount > 0f
                ? $"Weapon: your auto attacks heal the target for {item.Weapon.HealAmount} every {item.Weapon.SwingInterval}s"
                : $"Weapon: {item.Weapon.Damage} dmg every {item.Weapon.SwingInterval}s");
            if (item.Weapon.Effect != null) sb.AppendLine(DescribeEffect(item.Weapon.Effect, item.Weapon.Effect.Duration));
        }

        foreach (StatBonus bonus in item.Bonuses) sb.AppendLine(DescribeBonus(bonus));
        foreach (HpThresholdEffect threshold in item.HpThresholdEffects)
        {
            string pct = $"{threshold.HealthPercentThreshold * 100f:0}%";
            if (threshold.AboveThresholdBonuses.Count > 0)
            {
                sb.AppendLine($"Above {pct} health: {string.Join(", ", threshold.AboveThresholdBonuses.ConvertAll(DescribeBonus))}");
            }
            if (threshold.BelowThresholdBonuses.Count > 0)
            {
                sb.AppendLine($"At or below {pct} health: {string.Join(", ", threshold.BelowThresholdBonuses.ConvertAll(DescribeBonus))}");
            }
        }
        if (item.TwoHanded) sb.AppendLine("Two-handed: occupies Main Hand and Off Hand");
        if (item.GrantsAirHover) sb.AppendLine("While airborne, press Jump again to hover in place for 2s");
        if (item.SingleTargetBonuses.Count > 0)
        {
            string conditionBonuses = string.Join(", ", item.SingleTargetBonuses.ConvertAll(DescribeBonus));
            sb.AppendLine($"{conditionBonuses} while you've only damaged or debuffed one enemy in the last {item.SingleTargetWindowSeconds}s. Killing that enemy lets you switch targets without losing it.");
        }
        foreach (EffectImmunity immunity in item.Immunities)
        {
            if (immunity.Effect == null) continue;
            sb.AppendLine($"Immune to {immunity.Effect.DisplayName}{(immunity.GroundOnly ? " from ground effects" : "")}");
        }
        foreach (ItemAura aura in item.Auras)
        {
            if (aura.Effect == null) continue;
            sb.Append(aura.Range > 0f ? $"Aura ({aura.Range} range): " : "While worn: ").Append(aura.Effect.DisplayName);
            if (aura.Effect.TickDamage > 0f) sb.Append($", {aura.Effect.TickDamage} dmg/{aura.Effect.TickInterval}s");
            if (aura.Effect.TickHeal > 0f) sb.Append($", +{aura.Effect.TickHeal} hp/{aura.Effect.TickInterval}s");
            foreach (StatBonus bonus in aura.Effect.Modifiers) sb.Append(", ").Append(DescribeBonus(bonus));
            sb.AppendLine();
        }
        if ((item.Reveals & MinimapReveal.Players) != 0) sb.AppendLine("Reveals players on the minimap");
        if ((item.Reveals & MinimapReveal.Mobs) != 0) sb.AppendLine("Pulses monsters onto the minimap every 5s");
        if (item.BroadcastsLocation) sb.AppendLine("Broadcasts your location to allies' minimaps");

        return sb.ToString().TrimEnd();
    }

    private static string DescribeBonus(StatBonus bonus)
    {
        bool isPercent = bonus.ModifierType == StatModifierType.PercentAdditive;
        float displayValue = isPercent ? bonus.Value * 100f : bonus.Value;
        if (bonus.Stat == StatType.ManaCostMultiplier)
        {
            return $"Mana cost {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.ThreatMultiplier)
        {
            return $"Threat generated {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.DamageMultiplier)
        {
            return $"Damage dealt {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.HealingMultiplier)
        {
            return $"Healing done {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.DamageTakenMultiplier)
        {
            return $"Damage taken {displayValue:+0.#;-0.#}{(isPercent ? "%" : "")}";
        }
        if (bonus.Stat == StatType.BlockChancePercent)
        {
            // Flat 0-1 fraction (e.g. 0.05 = 5%), same as every other
            // ...Percent stat - not scaled by ModifierType/isPercent like
            // the multiplier stats above, which are already 1.0-based.
            return $"Block Chance +{bonus.Value * 100f:0.#}%";
        }
        string sign = displayValue >= 0f ? "+" : "";
        return $"{sign}{displayValue}{(isPercent ? "%" : "")} {bonus.Stat}";
    }
}
