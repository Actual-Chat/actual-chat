namespace ActualChat;

public static class Languages
{
    public static readonly Language English = new Language("en-US", "EN", "English", AssumeValid.Option);
    public static readonly Language French = new Language("fr-FR", "FR", "French", AssumeValid.Option);
    public static readonly Language German = new Language("de-DE", "DE", "German", AssumeValid.Option);
    public static readonly Language Russian = new Language("ru-RU", "RU", "Russian", AssumeValid.Option);
    public static readonly Language Spanish = new Language("es-ES", "ES", "Spanish", AssumeValid.Option);
    public static readonly Language Ukrainian = new Language("uk-UA", "UA", "Ukrainian", AssumeValid.Option);
    public static readonly Language Main = English;

    public static readonly ApiArray<Language> All = ApiArray.New(
        English,
        French,
        German,
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
