namespace ActualChat;

public static class Languages
{
    // For Arabic, we need RTL support.
    // public static readonly Language2 Arabic = new("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    // public static readonly Language2 Arabic = new("ar-SA", "AR", "Arabic (Saudi Arabia)", AssumeValid.Option); We need RTL support
    // public static readonly Language2 Bengali = new("bn-BD", "BN", "Bengali", AssumeValid.Option); Not supported
    public static readonly Language Chinese = new("zh-CN", "CN", "Chinese");
    public static readonly Language ChineseTW = new("zh-TW", "TW", "Chinese (Taiwan)");
    public static readonly Language Czech = new("cs-CZ", "CZ", "Czech");
    public static readonly Language Danish = new("da-DK", "DK", "Danish");
    public static readonly Language Dutch = new("nl-NL", "NL", "Dutch");
    public static readonly Language English = new("en-US", "EN", "English (USA)");
    public static readonly Language EnglishIN = new("en-IN", "EN-IN", "English (India)");
    public static readonly Language EnglishUK = new("en-GB", "EN-GB", "English (UK)");
    public static readonly Language Finnish = new("fi-FI", "FI", "Finnish");
    public static readonly Language French = new("fr-FR", "FR", "French");
    public static readonly Language FrenchCA = new("fr-CA", "FR-CA", "French (Canada)");
    public static readonly Language German = new("de-DE", "DE", "German");
    public static readonly Language Hindi = new("hi-IN", "HI", "Hindi");
    public static readonly Language Italian = new("it-IT", "IT", "Italian");
    public static readonly Language Japanese = new("ja-JP", "JP", "Japanese");
    public static readonly Language Korean = new("ko-KR", "KR", "Korean");
    public static readonly Language Polish = new("pl-PL", "PL", "Polish");
    public static readonly Language Portuguese = new("pt-PT", "PT", "Portuguese");
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)");
    public static readonly Language Russian = new("ru-RU", "RU", "Russian");
    public static readonly Language Spanish = new("es-ES", "ES", "Spanish");
    public static readonly Language SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)");
    public static readonly Language SpanishUS = new("es-US", "ES-US", "Spanish (USA)");
    public static readonly Language Swedish = new("sv-SE", "SE", "Swedish");
    public static readonly Language Thai = new("th-TH", "TH", "Thai");
    public static readonly Language Tamil = new ("ta-IN", "TA", "Tamil"); // Supports only Chirp Model in us-central1 Location
    public static readonly Language Turkish = new("tr-TR", "TR", "Turkish");
    public static readonly Language Ukrainian = new("uk-UA", "UA", "Ukrainian");
    public static readonly Language Vietnamese = new("vi-VN", "VN", "Vietnamese");

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
        Finnish,
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
        Swedish,
        Thai,
        Tamil,
        Turkish,
        Ukrainian,
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
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.Value.ToLowerInvariant(), x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language>(x.ShortTitle.ToLowerInvariant(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    public static readonly Dictionary<string, Language> SupportedById =
        ById.Where(x => AllSupported.Contains(x.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
