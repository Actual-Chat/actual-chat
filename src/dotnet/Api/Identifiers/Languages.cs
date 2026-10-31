namespace ActualChat;

/// <summary>
/// Provides static instances and lookup dictionary for supported languages.
/// </summary>
public static class Languages
{
    // Arabic and Hebrew are the right-to-left languages with a catalog; the page mirrors for them
    // (text-direction.ts). Persian and Urdu have none yet. Releasing Arabic or Hebrew makes device detection
    // pick it, so LocalizationUI.init must then guess the direction from the device language too.
    // A new language starts as LanguageSupport.Preview; releasing it means LanguageSupport.All and a place
    // in AllUI.
    public static readonly Language Afrikaans = new("af-ZA", "AF", "Afrikaans", "Afrikaans", LanguageSupport.Preview);
    public static readonly Language Albanian = new("sq-AL", "SQ", "Albanian", "Shqip", LanguageSupport.Preview);
    public static readonly Language Arabic = new("ar-SA", "AR", "Arabic", "العربية", LanguageSupport.Preview);
    public static readonly Language Azerbaijani =
        new("az-AZ", "AZ", "Azerbaijani", "Azərbaycanca", LanguageSupport.Preview);
    public static readonly Language Basque = new("eu-ES", "EU", "Basque", "Euskara", LanguageSupport.Preview);

    public static readonly Language Belarusian =
        new("be-BY", "BE", "Belarusian", "Беларуская", LanguageSupport.Preview);

    public static readonly Language Bengali = new("bn-BD", "BN", "Bengali", "বাংলা", LanguageSupport.Preview);
    public static readonly Language Bosnian = new("bs-BA", "BS", "Bosnian", "Bosanski", LanguageSupport.All);
    public static readonly Language Bulgarian = new("bg-BG", "BG", "Bulgarian", "Български", LanguageSupport.All);
    public static readonly Language Catalan = new("ca-ES", "CA", "Catalan", "Català", LanguageSupport.Preview);
    public static readonly Language Chinese = new("zh-CN", "zh", "Chinese", "中文", LanguageSupport.All);
    public static readonly Language ChineseTW = new("zh-TW", "zh-TW", "Chinese (Taiwan)");
    public static readonly Language Croatian = new("hr-HR", "HR", "Croatian", "Hrvatski", LanguageSupport.All);
    public static readonly Language Czech = new("cs-CZ", "CS", "Czech", "Čeština", LanguageSupport.All);
    public static readonly Language Danish = new("da-DK", "DA", "Danish", "Dansk", LanguageSupport.All);
    public static readonly Language Dutch = new("nl-NL", "NL", "Dutch", "Nederlands", LanguageSupport.All);
    public static readonly Language English = new("en-US", "EN", "English (USA)", "English", LanguageSupport.All);
    public static readonly Language EnglishIN = new("en-IN", "EN-IN", "English (India)");
    public static readonly Language EnglishUK = new("en-GB", "EN-GB", "English (UK)");
    public static readonly Language Estonian = new("et-EE", "ET", "Estonian", "Eesti", LanguageSupport.Preview);
    public static readonly Language Filipino = new("fil-PH", "FIL", "Filipino", "Filipino", LanguageSupport.All);
    public static readonly Language Finnish = new("fi-FI", "FI", "Finnish", "Suomi", LanguageSupport.All);
    public static readonly Language French = new("fr-FR", "FR", "French", "Français", LanguageSupport.All);
    public static readonly Language FrenchCA = new("fr-CA", "FR-CA", "French (Canada)");
    public static readonly Language Galician = new("gl-ES", "GL", "Galician", "Galego", LanguageSupport.Preview);
    public static readonly Language German = new("de-DE", "DE", "German", "Deutsch", LanguageSupport.All);
    public static readonly Language Greek = new("el-GR", "EL", "Greek", "Ελληνικά", LanguageSupport.Preview);
    public static readonly Language Gujarati = new("gu-IN", "GU", "Gujarati", "ગુજરાતી", LanguageSupport.Preview);
    public static readonly Language Hebrew = new("he-IL", "HE", "Hebrew", "עברית", LanguageSupport.Preview);
    public static readonly Language Hindi = new("hi-IN", "HI", "Hindi", "हिन्दी", LanguageSupport.All);
    public static readonly Language Hungarian = new("hu-HU", "HU", "Hungarian", "Magyar", LanguageSupport.Preview);
    public static readonly Language Indonesian =
        new("id-ID", "ID", "Indonesian", "Bahasa Indonesia", LanguageSupport.All);
    public static readonly Language Italian = new("it-IT", "IT", "Italian", "Italiano", LanguageSupport.All);
    public static readonly Language Japanese = new("ja-JP", "JA", "Japanese", "日本語", LanguageSupport.All);
    public static readonly Language Kannada = new("kn-IN", "KN", "Kannada", "ಕನ್ನಡ", LanguageSupport.Preview);
    public static readonly Language Kazakh = new("kk-KZ", "KK", "Kazakh", "Қазақ тілі", LanguageSupport.All);
    public static readonly Language Korean = new("ko-KR", "KO", "Korean", "한국어", LanguageSupport.All);
    public static readonly Language Latvian = new("lv-LV", "LV", "Latvian", "Latviešu", LanguageSupport.Preview);
    public static readonly Language Lithuanian = new("lt-LT", "LT", "Lithuanian", "Lietuvių", LanguageSupport.Preview);

    public static readonly Language Macedonian =
        new("mk-MK", "MK", "Macedonian", "Македонски", LanguageSupport.Preview);

    public static readonly Language Malay = new("ms-MY", "MS", "Malay", "Bahasa Melayu", LanguageSupport.All);
    public static readonly Language Malayalam = new("ml-IN", "ML", "Malayalam", "മലയാളം", LanguageSupport.Preview);
    public static readonly Language Marathi = new("mr-IN", "MR", "Marathi", "मराठी", LanguageSupport.All);
    public static readonly Language Max = new("max", "MAX", "Max", "Max", LanguageSupport.None);
    // "cnr" is Montenegrin's ISO 639-2 code, assigned in 2017
    public static readonly Language Montenegrin =
        new("cnr-ME", "CNR", "Montenegrin", "Crnogorski", LanguageSupport.All);
    public static readonly Language Norwegian = new("no-NO", "NO", "Norwegian", "Norsk", LanguageSupport.Preview);
    public static readonly Language Polish = new("pl-PL", "PL", "Polish", "Polski", LanguageSupport.All);
    public static readonly Language Portuguese = new("pt-PT", "PT", "Portuguese", "Português", LanguageSupport.All);
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)");
    public static readonly Language Punjabi = new("pa-IN", "PA", "Punjabi", "ਪੰਜਾਬੀ", LanguageSupport.All);
    // public static readonly Language Quechua = new("quz-PE", "qu", "Quechua", "Runa Simi"); No transcriber supports it
    public static readonly Language Romanian = new("ro-RO", "RO", "Romanian", "Română", LanguageSupport.Preview);
    public static readonly Language Russian = new("ru-RU", "RU", "Russian", "Русский", LanguageSupport.All);
    public static readonly Language Serbian = new("sr-SR", "SR", "Serbian", "Српски", LanguageSupport.All);
    public static readonly Language Slovak = new("sk-SK", "SK", "Slovak", "Slovenčina", LanguageSupport.Preview);
    public static readonly Language Slovenian = new("sl-SI", "SL", "Slovenian", "Slovenščina", LanguageSupport.Preview);
    public static readonly Language Spanish = new("es-ES", "ES", "Spanish", "Español", LanguageSupport.All);
    public static readonly Language SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)");
    public static readonly Language SpanishUS = new("es-US", "ES-US", "Spanish (USA)");
    public static readonly Language Swahili = new("sw-KE", "SW", "Swahili", "Kiswahili", LanguageSupport.Preview);
    public static readonly Language Swedish = new("sv-SE", "SV", "Swedish", "Svenska", LanguageSupport.All);
    public static readonly Language Tagalog = new("tl-PH", "TL", "Tagalog", "Tagalog");
    // Supports only the Chirp model, in the us-central1 location
    public static readonly Language Tamil = new("ta-IN", "TA", "Tamil", "தமிழ்", LanguageSupport.All);
    public static readonly Language Telugu = new("te-IN", "TE", "Telugu", "తెలుగు", LanguageSupport.Preview);
    public static readonly Language Thai = new("th-TH", "TH", "Thai", "ไทย", LanguageSupport.All);
    public static readonly Language Turkish = new("tr-TR", "TR", "Turkish", "Türkçe", LanguageSupport.All);
    public static readonly Language Ukrainian = new("uk-UA", "UK", "Ukrainian", "Українська", LanguageSupport.All);
    public static readonly Language Urdu = new("ur-PK", "UR", "Urdu", "اردو");
    public static readonly Language Uzbek = new("uz-UZ", "UZ", "Uzbek", "Oʻzbekcha", LanguageSupport.All);
    public static readonly Language Vietnamese = new("vi-VN", "VI", "Vietnamese", "Tiếng Việt", LanguageSupport.All);
    public static readonly Language Welsh = new("cy-GB", "CY", "Welsh", "Cymraeg", LanguageSupport.Preview);

    public static readonly Language Main = English;

    public static readonly Language[] All = [
        Afrikaans,
        Albanian,
        Arabic,
        Azerbaijani,
        Basque,
        Belarusian,
        Bengali,
        Bosnian,
        Bulgarian,
        Catalan,
        Chinese,
        ChineseTW,
        Croatian,
        Czech,
        Danish,
        Dutch,
        English,
        EnglishIN,
        EnglishUK,
        Estonian,
        Filipino,
        Finnish,
        French,
        FrenchCA,
        Galician,
        German,
        Greek,
        Gujarati,
        Hebrew,
        Hindi,
        Hungarian,
        Indonesian,
        Italian,
        Japanese,
        Kannada,
        Kazakh,
        Korean,
        Latvian,
        Lithuanian,
        Macedonian,
        Malay,
        Malayalam,
        Marathi,
        Montenegrin,
        Norwegian,
        Polish,
        Portuguese,
        PortugueseBR,
        Punjabi,
        // Quechua,
        Romanian,
        Russian,
        Serbian,
        Slovak,
        Slovenian,
        Spanish,
        SpanishMX,
        SpanishUS,
        Swahili,
        Swedish,
        Tagalog,
        Tamil,
        Telugu,
        Thai,
        Turkish,
        Ukrainian,
        Urdu,
        Uzbek,
        Vietnamese,
        Welsh,
    ];

    // The order the App-language picker shows them in, so it is declared rather than derived.
    // LanguagesTest keeps this set and the LanguageSupport.UI flag in agreement.
    public static readonly Language[] AllUI = [
        English, Spanish, French, Italian,
        Russian, German, Chinese, Hindi,
        Japanese, Korean, Portuguese, Turkish,
        Ukrainian, Vietnamese, Polish, Indonesian,
        Czech, Bulgarian, Bosnian, Croatian,
        Montenegrin, Serbian, Danish, Dutch,
        Filipino, Finnish, Kazakh, Malay,
        Marathi, Punjabi, Swedish, Tamil,
        Thai, Uzbek,
    ];

    // Includes URL-only pseudo-locales; never use this collection for a user-facing language choice.
    public static readonly Language[] AllUIAndTestOnly = [..AllUI, Max];

    // Every language that ships a catalog, LanguageSupport.Preview ones included
    public static readonly Language[] AllWithCatalog =
        [..AllUIAndTestOnly, ..All.Where(x => x.Support.HasFlag(LanguageSupport.Preview))];

    public static readonly Language[] AllTranscription =
        [..All.Where(x => x.Support.HasFlag(LanguageSupport.Transcription))];

    public static readonly Dictionary<string, Language> ById =
        All.Select(x => new KeyValuePair<string, Language>(x.Value, x))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.Value.ToLower(), x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle.ToLower(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    // The URL override reaches LanguageSupport.Preview languages too, so their catalogs can be tried
    // before they ship; Max is matched only by its exact code
    private static readonly Language[] UIOverrideCandidates = AllWithCatalog.Where(x => x != Max).ToArray();

    // Device locales that name a UI language by another code: Bokmål and Nynorsk are both
    // written Norwegian, and older Android reports Filipino as Tagalog, its base dialect.
    private static readonly Dictionary<string, Language> UILanguageAliases = new() {
        ["nb"] = Norwegian,
        ["nn"] = Norwegian,
        ["tl"] = Filipino,
    };

    public static Language ResolveUILanguage(
        string? languageOverride,
        Language? selected,
        IReadOnlyList<string> clientLanguages)
        => languageOverride is null
            ? selected ?? DetectUILanguage(clientLanguages)
            : languageOverride.Equals(Max.Value, StringComparison.OrdinalIgnoreCase)
                ? Max
                : DetectUILanguage([languageOverride], UIOverrideCandidates);

    public static Language GetCanonical(Language language)
    {
        // en-GB and en-US share one catalog, one translation and one dub; the UI variant is the
        // canonical one, and a language with no UI variant is canonical by itself
        if (language.Support.HasFlag(LanguageSupport.UI))
            return language;

        return AllUI.FirstOrDefault(x => x.IsoCode == language.IsoCode)
            ?? All.FirstOrDefault(x => x.IsoCode == language.IsoCode)
            ?? language;
    }

    public static Language DetectUILanguage(IReadOnlyList<string> clientLanguages)
        => DetectUILanguage(clientLanguages, AllUI);

    // Private methods

    private static Language DetectUILanguage(IReadOnlyList<string> clientLanguages, Language[] candidates)
    {
        // Matched by IsoCode rather than by Value: the client reports "en-GB" or a bare "en",
        // and AllUI carries one entry per catalog, so both must land on English.
        foreach (var clientLanguage in clientLanguages) {
            var isoCode = Language.GetIsoCode(clientLanguage);
            if (candidates.FirstOrDefault(x => x.IsoCode == isoCode) is { } language)
                return language;
            if (UILanguageAliases.TryGetValue(isoCode, out language) && candidates.Contains(language))
                return language;
        }

        return Main;
    }
}
