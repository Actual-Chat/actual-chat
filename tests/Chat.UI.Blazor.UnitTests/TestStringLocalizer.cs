using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

// TODO: review manually before PR
/// <summary>
/// An <see cref="IStringLocalizer"/> over an in-memory catalog that also records
/// the key of the last lookup, so tests can assert which key a member reads.
/// </summary>
internal sealed class TestStringLocalizer(
    Dictionary<string, string> strings,
    Language? language = null
    ) : IStringLocalizer, IHasUILanguage
{
    private Dictionary<string, string> _strings = strings;

    public string LastKey { get; private set; } = "";
    public bool IsLastKeyFound { get; private set; }
    public Language UILanguage { get; private set; } = language ?? Languages.English;

    public void SwitchTo(Language language, Dictionary<string, string> strings)
    {
        UILanguage = language;
        _strings = strings;
    }

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
        => _strings.Select(kv => new LocalizedString(kv.Key, kv.Value));

    // Private methods

    private bool TryGet(string name, out string value)
    {
        LastKey = name;
        IsLastKeyFound = _strings.TryGetValue(name, out var foundValue);
        value = foundValue ?? name;
        return IsLastKeyFound;
    }
}
