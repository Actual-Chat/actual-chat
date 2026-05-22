namespace ActualChat.MLSearch.Engine;

internal sealed class OpenSearchNames
{
    public const string EntryIndexVersion = "v4";
    public const string UserIndexVersion = "v6";
    public const string GroupIndexVersion = "v5";
    public const string PlaceIndexVersion = "v3";

    public const string TestPrefix = "test";
    public string TestIsolationKey { get; init; } = ""; // for testing purpose only
    public string Env { get; init; } = "";
    private string Prefix => ComposePrefix(Env, "sm", TestIsolationKey); // sm == "Search Module"
    public string CommonIndexTemplateName => $"{Prefix}common";
    public string CommonIndexPattern => $"{Prefix}*";
    public string UserIndexName => $"{Prefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{Prefix}chats-{GroupIndexVersion}";
    public string PlaceIndexName => $"{Prefix}places-{PlaceIndexVersion}";
    public string EntryIndexName => $"{Prefix}entries-{EntryIndexVersion}";

    private static string ComposePrefix(params IEnumerable<string> parts)
        => string.Join("", parts.Select(ComposePrefix));

    private static string ComposePrefix(string s)
    {
        s = s.Trim('-');
        return s.IsNullOrEmpty() ? "" : $"{s}-";
    }
}
