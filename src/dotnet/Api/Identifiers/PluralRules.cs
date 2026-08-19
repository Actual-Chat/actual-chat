namespace ActualChat;

/// <summary>
/// Picks which of a localized value's plural forms a count needs. Forms are listed in
/// CLDR order - one, few, many - minus the categories no shipped language uses.
/// </summary>
public static class PluralRules
{
    public static int GetPluralFormCount(this Language language)
        => language.IsoCode is "ru" or "uk" ? 3 : 2;

    public static int GetPluralFormIndex(this Language language, long count)
    {
        var n = Math.Abs(count);
        if (language.IsoCode is "ru" or "uk") {
            var mod10 = n % 10;
            var mod100 = n % 100;
            return mod10 == 1 && mod100 != 11 ? 0
                : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? 1
                : 2;
        }

        // French, Hindi and Portuguese put zero in the singular form; the rest don't
        var isOne = language.IsoCode is "fr" or "hi" or "pt" ? n <= 1 : n == 1;
        return isOne ? 0 : 1;
    }
}
