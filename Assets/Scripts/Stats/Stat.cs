using System.Collections.Generic;
using System.Linq;

public class Stat
{
    private readonly float baseValue;
    private readonly List<StatModifier> modifiers = new List<StatModifier>();
    private float cachedValue;
    private bool dirty = true;

    public Stat(float baseValue)
    {
        this.baseValue = baseValue;
    }

    public float Value
    {
        get
        {
            if (dirty) Recalculate();
            return cachedValue;
        }
    }

    public void AddModifier(StatModifier modifier)
    {
        modifiers.Add(modifier);
        dirty = true;
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        bool removed = modifiers.Remove(modifier);
        if (removed) dirty = true;
        return removed;
    }

    public int RemoveAllModifiersFromSource(object source)
    {
        int removed = modifiers.RemoveAll(m => Equals(m.Source, source));
        if (removed > 0) dirty = true;
        return removed;
    }

    private void Recalculate()
    {
        float flatSum = modifiers.Where(m => m.Type == StatModifierType.Flat).Sum(m => m.Value);
        float percentSum = modifiers.Where(m => m.Type == StatModifierType.PercentAdditive).Sum(m => m.Value);
        cachedValue = (baseValue + flatSum) * (1f + percentSum);
        dirty = false;
    }
}
