namespace ActualChat.UI.Blazor.Resources;

/// <summary>
/// Loads the embedded string catalogs: <c>Strings.&lt;lang&gt;.json</c> (ordinary UI text)
/// and <c>Messages.&lt;lang&gt;.json</c> (text reverse-indexed by <see cref="MessageIndex"/>).
/// </summary>
public static class StringCatalog
{
    // TODO: instead of such public constants let's have 2 separate methods LoadStrings and LoadMessages maybe even better names
    // TODO: all these 3 constants must be private or internal
    public const string StringsPrefix = "Strings.";
    public const string MessagesPrefix = "Messages.";
    public const string Suffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Assembly Assembly => typeof(Strings).Assembly;

    // TODO: if possible maybe return languages?
    public static IEnumerable<string> Subtags(string prefix)
        => Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(Suffix))
            .Select(n => n[prefix.Length..^Suffix.Length]);

    // TODO: if possible accept lanuguage?
    public static Dictionary<string, string>? Load(string prefix, string subtag)
    {
        using var stream = Assembly.GetManifestResourceStream($"{prefix}{subtag}{Suffix}");
        return stream == null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions);
    }
}
