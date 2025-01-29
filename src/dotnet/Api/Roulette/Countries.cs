namespace ActualChat.Roulette;

public static partial class Countries
{
    public static readonly Country NotSpecified = Country.NotSpecified;

    public static readonly Dictionary<string, Country> CodeToCountry;

    static Countries()
        => CodeToCountry =
            All.Select(x => new KeyValuePair<string, Country>(x.Code, x))
                .Concat(All.Select(x => new KeyValuePair<string, Country>(x.Code.ToLowerInvariant(), x)))
                .DistinctBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
