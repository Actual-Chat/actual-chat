namespace ActualChat.UI.Blazor.Resources;

/// <summary>
/// Loads the embedded string catalogs: <c>Strings.&lt;lang&gt;.json</c> (ordinary UI text)
/// and <c>Messages.&lt;lang&gt;.json</c> (text reverse-indexed by <see cref="MessageIndex"/>).
/// </summary>
public static class StringCatalogs
{
    private const string Suffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Assembly Assembly => typeof(Strings).Assembly;

    public static Dictionary<string, string>? LoadStrings(Language language)
        => Load(Kind.Strings, language.IsoCode);

    public static Dictionary<string, string>? LoadMessages(Language language)
        => Load(Kind.Messages, language.IsoCode);

    // Subtag-based rather than Language-based: a resource whose subtag names no known language
    // has to stay visible, and mapping it here would silently drop it instead.
    internal static IEnumerable<string> ShippedSubtags(Kind kind)
    {
        var prefix = Prefix(kind);
        return Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(n => n[prefix.Length..^Suffix.Length]);
    }

    internal static Dictionary<string, string>? Load(Kind kind, string subtag)
    {
        using var stream = Assembly.GetManifestResourceStream(ResourceName(kind, subtag));
        return stream == null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions);
    }

    internal static string ResourceName(Kind kind, string subtag)
        => $"{Prefix(kind)}{subtag}{Suffix}";

    // Private methods

    private static string Prefix(Kind kind)
        => kind switch {
            Kind.Strings => "Strings.",
            Kind.Messages => "Messages.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    // Nested types

    public enum Kind
    {
        Strings = 0,
        Messages = 1,
    }
}
