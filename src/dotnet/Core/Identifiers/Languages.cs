namespace ActualChat;

public static class Languages
{
    // For Arabic we need RTL support.
    //public static readonly Language Arabic = new Language("ar-SA", "SA", "Arabic", AssumeValid.Option);
    // Chinese does support only `chirp` recognition model which does not support streaming.
    //public static readonly Language Chinese = new Language("zh-CN", "CN", "Chinese", AssumeValid.Option);
    public static readonly Language English = new Language("en-US", "EN", "English", AssumeValid.Option);
    public static readonly Language French = new Language("fr-FR", "FR", "French", AssumeValid.Option);
    public static readonly Language German = new Language("de-DE", "DE", "German", AssumeValid.Option);
    public static readonly Language Japanese = new Language("ja-JP", "JP", "Japanese", AssumeValid.Option);
    public static readonly Language Korean = new Language("ko-KR", "KR", "Korean", AssumeValid.Option);
    public static readonly Language Portuguese = new Language("pt-BR", "BR", "Portuguese ", AssumeValid.Option);
    public static readonly Language Russian = new Language("ru-RU", "RU", "Russian", AssumeValid.Option);
    public static readonly Language Spanish = new Language("es-ES", "ES", "Spanish", AssumeValid.Option);
    public static readonly Language Ukrainian = new Language("uk-UA", "UA", "Ukrainian", AssumeValid.Option);

    public static readonly Language Main = English;
    public static readonly Language Loading = new("Loading", "Loading", "Loading", AssumeValid.Option);

    public static readonly ApiArray<Language> All = ApiArray.New(
        // Arabic,
        // Chinese,
        English,
        French,
        German,
        Japanese,
        Korean,
        Portuguese,
        Russian,
        Spanish,
        Ukrainian
    );

    public static readonly Dictionary<Symbol, Language> IdToLanguage =
        All.ToDictionary(x => x.Id)
            .Concat(All.ToDictionary(x => (Symbol)x.Value.ToLowerInvariant()))
            .Concat(All.ToDictionary(x => x.Code))
            .Concat(All.ToDictionary(x => (Symbol)x.Code.Value.ToLowerInvariant()))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
