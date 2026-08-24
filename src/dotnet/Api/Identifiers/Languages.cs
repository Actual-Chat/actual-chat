namespace ActualChat;

/// <summary>
/// Provides static instances and lookup dictionary for supported languages.
/// </summary>
public static class Languages
{
    // For Arabic, we need RTL support.
    // public static readonly Language2 Arabic = new("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    // We need RTL support:
    // public static readonly Language2 Arabic = new("ar-SA", "AR", "Arabic (Saudi Arabia)");
    // public static readonly Language2 Bengali = new("bn-BD", "BN", "Bengali", AssumeValid.Option); Not supported
    public static readonly Language Bosnian = new("bs-BA", "BS", "Bosnian", "Bosanski", LanguageSupport.All);
    public static readonly Language Bulgarian = new("bg-BG", "BG", "Bulgarian", "Български", LanguageSupport.All);
    public static readonly Language Chinese = new("zh-CN", "zh", "Chinese", "中文", LanguageSupport.All);
    public static readonly Language ChineseTW = new("zh-TW", "zh-TW", "Chinese (Taiwan)");
    public static readonly Language Croatian = new("hr-HR", "HR", "Croatian", "Hrvatski", LanguageSupport.All);
    public static readonly Language Czech = new("cs-CZ", "CS", "Czech", "Čeština", LanguageSupport.All);
    public static readonly Language Danish = new("da-DK", "DA", "Danish", "Dansk");
    public static readonly Language Dutch = new("nl-NL", "NL", "Dutch", "Nederlands");
    public static readonly Language English = new("en-US", "EN", "English (USA)", "English", LanguageSupport.All);
    public static readonly Language EnglishIN = new("en-IN", "EN-IN", "English (India)");
    public static readonly Language EnglishUK = new("en-GB", "EN-GB", "English (UK)");
    public static readonly Language Filipino = new("fil-PH", "FIL", "Filipino", "Filipino");
    public static readonly Language Finnish = new("fi-FI", "FI", "Finnish", "Suomi");
    public static readonly Language French = new("fr-FR", "FR", "French", "Français", LanguageSupport.All);
    public static readonly Language FrenchCA = new("fr-CA", "FR-CA", "French (Canada)");
    public static readonly Language German = new("de-DE", "DE", "German", "Deutsch", LanguageSupport.All);
    public static readonly Language Hindi = new("hi-IN", "HI", "Hindi", "हिन्दी", LanguageSupport.All);
    public static readonly Language Indonesian =
        new("id-ID", "ID", "Indonesian", "Bahasa Indonesia", LanguageSupport.All);
    public static readonly Language Italian = new("it-IT", "IT", "Italian", "Italiano", LanguageSupport.All);
    public static readonly Language Japanese = new("ja-JP", "JA", "Japanese", "日本語", LanguageSupport.All);
    public static readonly Language Kazakh = new("kk-KZ", "KK", "Kazakh", "Қазақ тілі");
    public static readonly Language Korean = new("ko-KR", "KO", "Korean", "한국어", LanguageSupport.All);
    public static readonly Language Malay = new("ms-MY", "MS", "Malay", "Bahasa Melayu");
    public static readonly Language Marathi = new("mr-IN", "MR", "Marathi", "मराठी");
    // "cnr" is Montenegrin's ISO 639-2 code, assigned in 2017
    public static readonly Language Montenegrin = new("cnr-ME", "CNR", "Montenegrin", "Crnogorski", LanguageSupport.UI);
    public static readonly Language Polish = new("pl-PL", "PL", "Polish", "Polski", LanguageSupport.All);
    public static readonly Language Portuguese = new("pt-PT", "PT", "Portuguese", "Português", LanguageSupport.All);
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)");
    public static readonly Language Punjabi = new("pa-IN", "PA", "Punjabi", "ਪੰਜਾਬੀ");
    // public static readonly Language Quechua = new("quz-PE", "qu", "Quechua", "Runa Simi"); No transcriber supports it
    public static readonly Language Russian = new("ru-RU", "RU", "Russian", "Русский", LanguageSupport.All);
    public static readonly Language Serbian = new("sr-SR", "SR", "Serbian", "Српски", LanguageSupport.All);
    public static readonly Language Spanish = new("es-ES", "ES", "Spanish", "Español", LanguageSupport.All);
    public static readonly Language SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)");
    public static readonly Language SpanishUS = new("es-US", "ES-US", "Spanish (USA)");
    public static readonly Language Swedish = new("sv-SE", "SV", "Swedish", "Svenska");
    public static readonly Language Tagalog = new("tl-PH", "TL", "Tagalog", "Tagalog");
    // Supports only the Chirp model, in the us-central1 location
    public static readonly Language Tamil = new("ta-IN", "TA", "Tamil", "தமிழ்");
    public static readonly Language Thai = new("th-TH", "TH", "Thai", "ไทย");
    public static readonly Language Turkish = new("tr-TR", "TR", "Turkish", "Türkçe", LanguageSupport.All);
    public static readonly Language Ukrainian = new("uk-UA", "UK", "Ukrainian", "Українська", LanguageSupport.All);
    public static readonly Language Urdu = new("ur-PK", "UR", "Urdu", "اردو");
    public static readonly Language Uzbek = new("uz-UZ", "UZ", "Uzbek", "Oʻzbekcha");
    public static readonly Language Vietnamese = new("vi-VN", "VI", "Vietnamese", "Tiếng Việt", LanguageSupport.All);

    public static readonly Language Main = English;

    public static readonly Language[] All = [
        Bosnian,
        Bulgarian,
        Chinese,
        ChineseTW,
        Croatian,
        Czech,
        Danish,
        Dutch,
        English,
        EnglishIN,
        EnglishUK,
        Filipino,
        Finnish,
        French,
        FrenchCA,
        German,
        Hindi,
        Indonesian,
        Italian,
        Japanese,
        Kazakh,
        Korean,
        Malay,
        Marathi,
        Montenegrin,
        Polish,
        Portuguese,
        PortugueseBR,
        Punjabi,
        // Quechua,
        Russian,
        Serbian,
        Spanish,
        SpanishMX,
        SpanishUS,
        Swedish,
        Tagalog,
        Tamil,
        Thai,
        Turkish,
        Ukrainian,
        Urdu,
        Uzbek,
        Vietnamese,
    ];

    // The order the App-language picker shows them in, so it is declared rather than derived.
    // LanguagesTest keeps this set and the LanguageSupport.UI flag in agreement.
    public static readonly Language[] AllUI = [
        English, Spanish, French, Italian,
        Russian, German, Chinese, Hindi,
        Japanese, Korean, Portuguese, Turkish,
        Ukrainian, Vietnamese, Polish, Indonesian,
        Czech, Bulgarian, Bosnian, Croatian,
        Montenegrin, Serbian,
    ];

    public static readonly Language[] AllTranscription =
        All.Where(x => x.Support.HasFlag(LanguageSupport.Transcription)).ToArray();

    public static readonly Dictionary<string, Language> ById =
        All.Select(x => new KeyValuePair<string, Language>(x.Value, x))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.Value.ToLower(), x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle.ToLower(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public static Language ResolveUILanguage(
        string? languageOverride,
        Language? selected,
        IReadOnlyList<string> clientLanguages)
        => languageOverride is not null
            ? DetectUILanguage([languageOverride])
            : selected ?? DetectUILanguage(clientLanguages);

    public static Language DetectUILanguage(IReadOnlyList<string> clientLanguages)
    {
        // Matched by IsoCode rather than by Value: the client reports "en-GB" or a bare "en",
        // and AllUI carries one entry per catalog, so both must land on English.
        foreach (var clientLanguage in clientLanguages) {
            var isoCode = Language.GetIsoCode(clientLanguage);
            if (AllUI.FirstOrDefault(x => x.IsoCode == isoCode) is { } language)
                return language;
        }

        return Main;
    }
}
