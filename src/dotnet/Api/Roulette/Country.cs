using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Country
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Country>();

    internal Country(string code, string title, AssumeValid _)
    {
        Code = code;
        Title = title;
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Country(string code)
    {
        // Intended: if we remove the country, we want the deserialization to work
        var c = ParseOrNone(code);
        Code = c.Code;
        Title = c.Title;
    }

    public static readonly Country NotSpecified = new ("", "Not Specified", AssumeValid.Option);

    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsNotSpecified => this == NotSpecified;

    [DataMember, MemoryPackOrder(0)] public string Code { get; init; }
    [IgnoreDataMember, MemoryPackIgnore] public string Title { get; init; }

    // Parsing

    public static Country Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Country>(s);
    public static Country ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<Country>(s).LogWarning(Log, NotSpecified);

    public static bool TryParse(string? code, out Country result)
    {
        result = NotSpecified;
        if (code.IsNullOrEmpty())
            return true; // NotSpecified

        if (Countries.CodeToCountry.TryGetValue(code, out var temp)) {
            result = temp;
            return true;
        }

        if (Countries.CodeToCountry.TryGetValue(code.ToLowerInvariant(), out temp)) {
            result = temp;
            return true;
        }

        return false;
    }
}
