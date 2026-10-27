namespace ActualChat;

/// <summary>
/// Picks which of a localized value's plural forms a count needs. Forms are listed in
/// CLDR order - one, few, many - minus the categories no shipped language uses.
/// </summary>
public static class PluralRules
{
    public static int GetPluralFormCount(this Language language)
        => language == Languages.Max ? 1
            : language.IsoCode switch {
                "sl" => 4,
                "ru" or "uk" or "be" or "sr" or "hr" or "bs" or "cnr" => 3,
                "pl" or "cs" or "sk" or "lt" or "lv" or "ro" => 3,
                _ => 2,
            };

    public static int GetPluralFormIndex(this Language language, long count)
    {
        if (language == Languages.Max)
            return 0;

        var n = Math.Abs(count);
        var mod10 = n % 10;
        var mod100 = n % 100;
        var isSlavicFew = mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14;
        var isTeen = mod100 is >= 11 and <= 19;
        switch (language.IsoCode) {
        // East Slavic and BCMS share a rule: 21 and 101 are "one" (21 файл, 21 fajl)
        case "ru" or "uk" or "be" or "sr" or "hr" or "bs" or "cnr":
            return mod10 == 1 && mod100 != 11 ? 0 : isSlavicFew ? 1 : 2;

        // Polish differs exactly there - only a bare 1 is "one" (21 plików, not 21 plik)
        case "pl":
            return n == 1 ? 0 : isSlavicFew ? 1 : 2;

        // Czech and Slovak "few" is a plain 2-4, with no mod-100 exception (22 souborů, not 22 soubory)
        case "cs" or "sk":
            return n == 1 ? 0 : n is >= 2 and <= 4 ? 1 : 2;

        // Slovenian has a dual and counts by the last two digits: 101 član, 102 člana, 103 člani
        case "sl":
            return mod100 switch { 1 => 0, 2 => 1, 3 or 4 => 2, _ => 3 };

        // Lithuanian "few" runs 2-9 and the teens join 10, 20...: 2 nariai, 10 narių, 21 narys
        case "lt":
            return mod10 == 1 && !isTeen ? 0 : mod10 >= 2 && !isTeen ? 1 : 2;

        // Latvian's CLDR "zero" is 0, 10-20, 30...: listed last, it plays the part of "many"
        case "lv":
            return mod10 == 1 && mod100 != 11 ? 0 : mod10 == 0 || isTeen ? 2 : 1;

        // Romanian adds "de" from 20 on (20 de membri), which is the third form
        case "ro":
            return n == 1 ? 0 : n == 0 || mod100 is >= 2 and <= 19 ? 1 : 2;

        // Macedonian is "one" for anything ending in 1 but 11
        case "mk":
            return mod10 == 1 && mod100 != 11 ? 0 : 1;
        default:
            // These put zero in the singular form; the rest don't
            var isOne = language.IsoCode is "fr" or "hi" or "pt" or "bn" or "gu" or "kn" or "pa" ? n <= 1 : n == 1;
            return isOne ? 0 : 1;
        }
    }
}
