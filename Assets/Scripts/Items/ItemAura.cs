using System;

// A status effect the wearer radiates to every player within Range
// (themself included). The effect asset says what the aura does; the
// equipment pulses it onto everyone in range, so leaving the radius lets
// it lapse on its own and two wearers of the same aura don't stack.
[Serializable]
public struct ItemAura
{
    public StatusEffectData Effect;
    public float Range;
}
