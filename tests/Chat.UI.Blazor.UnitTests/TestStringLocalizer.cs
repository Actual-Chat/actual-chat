using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

/// <summary>
/// An <see cref="IStringLocalizer"/> over an in-memory catalog that also records
/// the key of the last lookup, so tests can assert which key a member reads.
/// </summary>
internal sealed class TestStringLocalizer(Dictionary<string, string> strings) : IStringLocalizer
{
    public string LastKey { get; private set; } = "";
    public bool IsLastKeyFound { get; private set; }

    public LocalizedString this[string name] {
        get {
            var isFound = TryGet(name, out var value);
            return new LocalizedString(name, value, !isFound);
        }
    }

    public LocalizedString this[string name, params object[] arguments] {
        get {
            var isFound = TryGet(name, out var value);
            return new LocalizedString(name, isFound ? string.Format(value, arguments) : value, !isFound);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => strings.Select(kv => new LocalizedString(kv.Key, kv.Value));

    // Private methods

    private bool TryGet(string name, out string value)
    {
        LastKey = name;
        IsLastKeyFound = strings.TryGetValue(name, out var foundValue);
        value = foundValue ?? name;
        return IsLastKeyFound;
    }
}
