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
    public static readonly Language Chinese = new("zh-CN", "zh", "Chinese");
    public static readonly Language ChineseTW = new("zh-TW", "zh-TW", "Chinese (Taiwan)");
    public static readonly Language Czech = new("cs-CZ", "CS", "Czech");
    public static readonly Language Danish = new("da-DK", "DA", "Danish");
    public static readonly Language Dutch = new("nl-NL", "NL", "Dutch");
    public static readonly Language English = new("en-US", "EN", "English (USA)");
    public static readonly Language EnglishIN = new("en-IN", "EN-IN", "English (India)");
    public static readonly Language EnglishUK = new("en-GB", "EN-GB", "English (UK)");
    public static readonly Language Filipino = new("fil-PH", "FIL", "Filipino");
    public static readonly Language Finnish = new("fi-FI", "FI", "Finnish");
    public static readonly Language French = new("fr-FR", "FR", "French");
    public static readonly Language FrenchCA = new("fr-CA", "FR-CA", "French (Canada)");
    public static readonly Language German = new("de-DE", "DE", "German");
    public static readonly Language Hindi = new("hi-IN", "HI", "Hindi");
    public static readonly Language Indonesian = new("id-ID", "ID", "Indonesian");
    public static readonly Language Italian = new("it-IT", "IT", "Italian");
    public static readonly Language Japanese = new("ja-JP", "JA", "Japanese");
    public static readonly Language Kazakh = new("kk-KZ", "KK", "Kazakh");
    public static readonly Language Korean = new("ko-KR", "KO", "Korean");
    public static readonly Language Malay = new("ms-MY", "MS", "Malay");
    public static readonly Language Marathi = new("mr-IN", "MR", "Marathi");
    public static readonly Language Polish = new("pl-PL", "PL", "Polish");
    public static readonly Language Portuguese = new("pt-PT", "PT", "Portuguese");
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)");
    public static readonly Language Punjabi = new("pa-IN", "PA", "Punjabi");
    // public static readonly Language Quechua = new("quz-PE", "qu", "Quechua"); No transcriber supports it
    public static readonly Language Russian = new("ru-RU", "RU", "Russian");
    public static readonly Language Serbian = new("sr-SR", "SR", "Serbian");
    public static readonly Language Spanish = new("es-ES", "ES", "Spanish");
    public static readonly Language SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)");
    public static readonly Language SpanishUS = new("es-US", "ES-US", "Spanish (USA)");
    public static readonly Language Swedish = new("sv-SE", "SV", "Swedish");
    public static readonly Language Tagalog = new("tl-PH", "TL", "Tagalog");
    public static readonly Language Tamil = new ("ta-IN", "TA", "Tamil"); // Supports only Chirp Model in us-central1 Location
    public static readonly Language Thai = new("th-TH", "TH", "Thai");
    public static readonly Language Turkish = new("tr-TR", "TR", "Turkish");
    public static readonly Language Ukrainian = new("uk-UA", "UK", "Ukrainian");
    public static readonly Language Urdu = new("ur-PK", "UR", "Urdu");
    public static readonly Language Uzbek = new("uz-UZ", "UZ", "Uzbek");
    public static readonly Language Vietnamese = new("vi-VN", "VI", "Vietnamese");

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
