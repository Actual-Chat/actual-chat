namespace ActualChat;

/// <summary>
/// Picks which of a localized value's plural forms a count needs. Forms are listed in
/// CLDR order - one, few, many - minus the categories no shipped language uses.
/// </summary>
public static class PluralRules
{
    public static int GetPluralFormCount(this Language language)
        => language.IsoCode is "ru" or "uk" or "sr" or "hr" or "bs" or "cnr" or "pl" or "cs" ? 3 : 2;

    public static int GetPluralFormIndex(this Language language, long count)
    {
        var n = Math.Abs(count);
        var mod10 = n % 10;
        var mod100 = n % 100;
        var isSlavicFew = mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14;
        switch (language.IsoCode) {
        // East Slavic and BCMS share a rule: 21 and 101 are "one" (21 файл, 21 fajl)
        case "ru" or "uk" or "sr" or "hr" or "bs" or "cnr":
            return mod10 == 1 && mod100 != 11 ? 0 : isSlavicFew ? 1 : 2;
        // Polish differs exactly there - only a bare 1 is "one" (21 plików, not 21 plik)
        case "pl":
            return n == 1 ? 0 : isSlavicFew ? 1 : 2;
        // Czech's "few" is a plain 2-4, with no mod-100 exception (22 souborů, not 22 soubory)
        case "cs":
            return n == 1 ? 0 : n is >= 2 and <= 4 ? 1 : 2;
        default:
            // French, Hindi and Portuguese put zero in the singular form; the rest don't
            var isOne = language.IsoCode is "fr" or "hi" or "pt" ? n <= 1 : n == 1;
            return isOne ? 0 : 1;
        }
    }
}
