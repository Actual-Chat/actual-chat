namespace ActualChat;

public static class Languages2
{
    // For Arabic we need RTL support.
    //public static readonly Language2 Arabic = new("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    //public static readonly Language2 Arabic       = new("ar-SA", "AR", "Arabic (Saudi Arabia)", AssumeValid.Option); We need RTL support
    //public static readonly Language2 Bengali      = new("bn-BD", "BN", "Bengali", AssumeValid.Option); Not supported
    public static readonly Language2 Chinese = new("zh-CN", "CN", "Chinese", AssumeValid.Option);
    public static readonly Language2 ChineseTW = new("zh-TW", "TW", "Chinese (Taiwan)", AssumeValid.Option);
    public static readonly Language2 Czech = new("cs-CZ", "CZ", "Czech", AssumeValid.Option);
    public static readonly Language2 Danish = new("da-DK", "DK", "Danish", AssumeValid.Option);
    public static readonly Language2 Dutch = new("nl-NL", "NL", "Dutch", AssumeValid.Option);
    public static readonly Language2 English = new("en-US", "EN", "English (USA)", AssumeValid.Option);
    public static readonly Language2 EnglishIN = new("en-IN", "EN-IN", "English (India)", AssumeValid.Option);
    public static readonly Language2 EnglishUK = new("en-GB", "EN-GB", "English (UK)", AssumeValid.Option);
    public static readonly Language2 Finnish = new("fi-FI", "FI", "Finnish", AssumeValid.Option);
    public static readonly Language2 French = new("fr-FR", "FR", "French", AssumeValid.Option);
    public static readonly Language2 FrenchCA = new("fr-CA", "FR-CA", "French (Canada)", AssumeValid.Option);
    public static readonly Language2 German = new("de-DE", "DE", "German", AssumeValid.Option);
    public static readonly Language2 Hindi = new("hi-IN", "HI", "Hindi", AssumeValid.Option);
    public static readonly Language2 Italian = new("it-IT", "IT", "Italian", AssumeValid.Option);
    public static readonly Language2 Japanese = new("ja-JP", "JP", "Japanese", AssumeValid.Option);
    public static readonly Language2 Korean = new("ko-KR", "KR", "Korean", AssumeValid.Option);
    public static readonly Language2 Polish = new("pl-PL", "PL", "Polish", AssumeValid.Option);
    public static readonly Language2 Portuguese = new("pt-PT", "PT", "Portuguese", AssumeValid.Option);
    public static readonly Language2 PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)", AssumeValid.Option);
    public static readonly Language2 Russian = new("ru-RU", "RU", "Russian", AssumeValid.Option);
    public static readonly Language2 Spanish = new("es-ES", "ES", "Spanish", AssumeValid.Option);
    public static readonly Language2 SpanishMX = new("es-MX", "ES-MX", "Spanish (Mexico)", AssumeValid.Option);
    public static readonly Language2 SpanishUS = new("es-US", "ES-US", "Spanish (USA)", AssumeValid.Option);
    public static readonly Language2 Swedish = new("sv-SE", "SE", "Swedish", AssumeValid.Option);
    public static readonly Language2 Thai = new("th-TH", "TH", "Thai", AssumeValid.Option);
    public static readonly Language2 Tamil = new ("ta-IN", "TA", "Tamil", AssumeValid.Option); // Supports only Chirp Model in us-central1 Location
    public static readonly Language2 Turkish = new("tr-TR", "TR", "Turkish", AssumeValid.Option);
    public static readonly Language2 Ukrainian = new("uk-UA", "UA", "Ukrainian", AssumeValid.Option);
    public static readonly Language2 Vietnamese = new("vi-VN", "VN", "Vietnamese", AssumeValid.Option);

    public static readonly Language2 Main = English;
    public static readonly Language2 Loading = new("Loading", "Loading", "Loading", AssumeValid.Option);

    public static readonly Language2[] All = [
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

    public static readonly Language2[] Supported = [
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
        Vietnamese
    ];

    public static readonly Dictionary<string, Language2> Map =
        All.Select(x => new KeyValuePair<string, Language2>(x.Value, x))
            .Concat(All.Select(x => new KeyValuePair<string, Language2>(x.Value.ToLowerInvariant(), x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language2>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<string, Language2>(x.ShortTitle.ToLowerInvariant(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    public static readonly Dictionary<string, Language2> SupportedMap =
        Map.Where(x => Supported.Contains(x.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
