// Armor -> damage reduction with diminishing returns:
// reduction = armor / (armor + K). K is the armor that gives 50%, derived
// from the one reference point that was chosen (100 armor = 70%), so the
// curve is retuned by changing those two constants, not a magic number.
// Never reaches 100% however much armor stacks.
public static class ArmorMitigation
{
    public const float ReferenceArmor = 100f;
    public const float ReferenceReduction = 0.7f;

    // K = ReferenceArmor * (1 - r) / r  (= 300/7, about 42.857).
    public const float ArmorForHalfReduction =
        ReferenceArmor * (1f - ReferenceReduction) / ReferenceReduction;

    // Fraction of damage removed, 0..<1. Zero or negative armor gives
    // exactly 0 (also avoids the divide-by-zero at armor == -K).
    public static float Reduction(float armor)
    {
        if (armor <= 0f) return 0f;
        return armor / (armor + ArmorForHalfReduction);
    }
}
