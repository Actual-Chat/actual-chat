namespace ActualChat;

/// <summary>
/// Provides static instances and lookup dictionary for supported languages.
/// </summary>
public static class Languages
{
    // For Arabic, we need RTL support.
    // public static readonly Language2 Arabic = new("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    // public static readonly Language2 Arabic = new("ar-SA", "AR", "Arabic (Saudi Arabia)", AssumeValid.Option); We need RTL support
    // public static readonly Language2 Bengali = new("bn-BD", "BN", "Bengali", AssumeValid.Option); Not supported
    public static readonly Language Chinese = new("zh-CN", "zh", "Chinese", "中文");
    public static readonly Language ChineseTW = new("zh-TW", "zh-TW", "Chinese (Taiwan)");
    public static readonly Language Czech = new("cs-CZ", "CS", "Czech", "Čeština");
    public static readonly Language Danish = new("da-DK", "DA", "Danish", "Dansk");
    public static readonly Language Dutch = new("nl-NL", "NL", "Dutch", "Nederlands");
    public static readonly Language English = new("en-US", "EN", "English (USA)", "English");
    public static readonly Language EnglishIN = new("en-IN", "EN-IN", "English (India)");
    public static readonly Language EnglishUK = new("en-GB", "EN-GB", "English (UK)");
    public static readonly Language Filipino = new("fil-PH", "FIL", "Filipino", "Filipino");
    public static readonly Language Finnish = new("fi-FI", "FI", "Finnish", "Suomi");
    public static readonly Language French = new("fr-FR", "FR", "French", "Français");
    public static readonly Language FrenchCA = new("fr-CA", "FR-CA", "French (Canada)");
    public static readonly Language German = new("de-DE", "DE", "German", "Deutsch");
    public static readonly Language Hindi = new("hi-IN", "HI", "Hindi", "हिन्दी");
    public static readonly Language Indonesian = new("id-ID", "ID", "Indonesian", "Bahasa Indonesia");
    public static readonly Language Italian = new("it-IT", "IT", "Italian", "Italiano");
    public static readonly Language Japanese = new("ja-JP", "JA", "Japanese", "日本語");
    public static readonly Language Kazakh = new("kk-KZ", "KK", "Kazakh", "Қазақ тілі");
    public static readonly Language Korean = new("ko-KR", "KO", "Korean", "한국어");
    public static readonly Language Malay = new("ms-MY", "MS", "Malay", "Bahasa Melayu");
    public static readonly Language Marathi = new("mr-IN", "MR", "Marathi", "मराठी");
    public static readonly Language Polish = new("pl-PL", "PL", "Polish", "Polski");
    public static readonly Language Portuguese = new("pt-PT", "PT", "Portuguese", "Português");
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)");
    public static readonly Language Punjabi = new("pa-IN", "PA", "Punjabi", "ਪੰਜਾਬੀ");
    // public static readonly Language Quechua = new("quz-PE", "qu", "Quechua", "Runa Simi"); No transcriber supports it
    public static readonly Language Russian = new("ru-RU", "RU", "Russian", "Русский");
    public static readonly Language Serbian = new("sr-SR", "SR", "Serbian", "Српски");
    public static readonly Language Spanish = new("es-ES", "ES", "Spanish", "Español");
    public static readonly Language SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)");
    public static readonly Language SpanishUS = new("es-US", "ES-US", "Spanish (USA)");
    public static readonly Language Swedish = new("sv-SE", "SV", "Swedish", "Svenska");
    public static readonly Language Tagalog = new("tl-PH", "TL", "Tagalog", "Tagalog");
    public static readonly Language Tamil = new ("ta-IN", "TA", "Tamil", "தமிழ்"); // Supports only Chirp Model in us-central1 Location
    public static readonly Language Thai = new("th-TH", "TH", "Thai", "ไทย");
    public static readonly Language Turkish = new("tr-TR", "TR", "Turkish", "Türkçe");
    public static readonly Language Ukrainian = new("uk-UA", "UK", "Ukrainian", "Українська");
    public static readonly Language Urdu = new("ur-PK", "UR", "Urdu", "اردو");
    public static readonly Language Uzbek = new("uz-UZ", "UZ", "Uzbek", "Oʻzbekcha");
    public static readonly Language Vietnamese = new("vi-VN", "VI", "Vietnamese", "Tiếng Việt");

    public static readonly Language Main = English;

    public static readonly Language[] All = [
        Chinese,
        ChineseTW,
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

    public static readonly Language[] AllSupported = [
        // Arabic,
        // Chinese,
        English,
        EnglishUK,
        EnglishIN,
        // Bengali,
        French,
        FrenchCA,
        German,
        Hindi,
        Italian,
        Japanese,
        Korean,
        Polish,
        Portuguese,
        PortugueseBR,
        Russian,
        Spanish,
        SpanishMX,
        SpanishUS,
        // Tamil,
        Thai,
        Turkish,
        Ukrainian,
        Vietnamese,
    ];

    public static readonly Dictionary<string, Language> ById =
        All.Select(x => new KeyValuePair<string, Language>(x.Value, x))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.Value.ToLower(), x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle.ToLower(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public static readonly Dictionary<string, Language> SupportedById =
        ById.Where(x => AllSupported.Contains(x.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
