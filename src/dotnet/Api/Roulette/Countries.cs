namespace ActualChat.Roulette;

public static class Countries
{
    public static readonly Country NotSpecified = Country.NotSpecified;
    public static readonly Country USA = new ("US", "United States", AssumeValid.Option);
    public static readonly Country Russia = new ("RU", "Russia", AssumeValid.Option);
    public static readonly Country Armenia = new ("AM", "Armenia", AssumeValid.Option);

    public static readonly ApiArray<Country> All = [
        Armenia,
        Russia,
        USA
    ];

    public static readonly Dictionary<string, Country> CodeToCountry =
        All.Select(x => new KeyValuePair<string, Country>(x.Code, x))
            .Concat(All.Select(x => new KeyValuePair<string, Country>(x.Code.ToLowerInvariant(), x)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}
