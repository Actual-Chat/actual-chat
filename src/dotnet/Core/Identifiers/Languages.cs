namespace ActualChat;

public static class Languages
{
    // For Arabic we need RTL support.
    //public static readonly Language Arabic = new("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    //public static readonly Language Chinese = new("zh-CN", "CN", "Chinese", AssumeValid.Option);
    public static readonly Language English      = new("en-US", "EN", "English (USA)", AssumeValid.Option);
    public static readonly Language EnglishUK    = new("en-GB", "EN-GB", "English (United Kingdom)", AssumeValid.Option);
    public static readonly Language EnglishIN    = new("en-IN", "EN-IN", "English (India)", AssumeValid.Option);
    public static readonly Language French       = new("fr-FR", "FR", "French", AssumeValid.Option);
    public static readonly Language FrenchCA     = new("fr-CA", "FR-CA", "French (Canada)", AssumeValid.Option);
    public static readonly Language German       = new("de-DE", "DE", "German", AssumeValid.Option);
    public static readonly Language Japanese     = new("ja-JP", "JP", "Japanese", AssumeValid.Option);
    public static readonly Language Korean       = new("ko-KR", "KR", "Korean", AssumeValid.Option);
    public static readonly Language Portuguese   = new("pt-PT", "PT", "Portuguese", AssumeValid.Option);
    public static readonly Language PortugueseBR = new("pt-BR", "PT-BR", "Portuguese (Brazil)", AssumeValid.Option);
    public static readonly Language Russian      = new("ru-RU", "RU", "Russian", AssumeValid.Option);
    public static readonly Language Spanish      = new("es-ES", "ES", "Spanish", AssumeValid.Option);
    public static readonly Language SpanishMX    = new("es-MX", "ES-MX", "Spanish (Mexico)", AssumeValid.Option);
    public static readonly Language SpanishUS    = new("es-US", "ES-US", "Spanish (USA)", AssumeValid.Option);
    public static readonly Language Ukrainian    = new("uk-UA", "UA", "Ukrainian", AssumeValid.Option);
    public static readonly Language Hindi        = new("hi-IN", "HI", "Hindi", AssumeValid.Option);
    //public static readonly Language Bengali      = new("bn-BD", "BN", "Bengali", AssumeValid.Option); Not supported
    //public static readonly Language Tamil        = new("ta-IN", "TA", "Tamil", AssumeValid.Option); Supports only Chirp Model in us-central1 Location
    //public static readonly Language Arabic       = new("ar-SA", "AR", "Arabic (Saudi Arabia)", AssumeValid.Option); We need RTL support
    public static readonly Language Turkish      = new("tr-TR", "TR", "Turkish", AssumeValid.Option);
    public static readonly Language Vietnamese   = new("vi-VN", "VN", "Vietnamese", AssumeValid.Option);
    public static readonly Language Italian      = new("it-IT", "IT", "Italian", AssumeValid.Option);
    public static readonly Language Thai         = new("th-TH", "TH", "Thai", AssumeValid.Option);
    public static readonly Language Polish       = new("pl-PL", "PL", "Polish", AssumeValid.Option);

    public static readonly Language Main = English;
    public static readonly Language Loading = new("Loading", "Loading", "Loading", AssumeValid.Option);

    public static readonly ApiArray<Language> All = ApiArray.New(
        // Arabic,
        // Chinese,
        English,
        EnglishUK,
        EnglishIN,
        //Bengali,
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
        //Tamil,
        Thai,
        Turkish,
        Ukrainian,
        Vietnamese
    );

    public static readonly Dictionary<Symbol, Language> IdToLanguage =
        All.Select(x => new KeyValuePair<Symbol, Language>(x.Id, x))
            .Concat(All.Select(x => new KeyValuePair<Symbol, Language>(x.Id.Value.ToLowerInvariant(), x)))
            .Concat(All.Select(x => new KeyValuePair<Symbol, Language>(x.ShortTitle, x)))
            .Concat(All.Select(x => new KeyValuePair<Symbol, Language>(x.ShortTitle.Value.ToLowerInvariant(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
