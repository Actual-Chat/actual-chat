using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// The merged Strings + Messages catalog of each shipped UI language, loaded on first use.
/// Callers pick the language; this type never resolves one.
/// </summary>
public static class StringCatalog
{
    private static readonly ConcurrentDictionary<Language, Dictionary<string, string>?> Translations = new();

    public static LocalizedString Get(Language language, string name)
    {
        var value = GetString(language, name, out var found);
        return new LocalizedString(name, value, resourceNotFound: !found);
    }

    public static LocalizedString Get(Language language, string name, object[] arguments)
    {
        var value = GetString(language, name, out var found);
        var formatted = found ? string.Format(value, arguments) : value;
        return new LocalizedString(name, formatted, resourceNotFound: !found);
    }

    public static IEnumerable<LocalizedString> GetAll(Language language)
    {
        var dict = GetTranslations(language) ?? GetTranslations(Languages.Main);
        return dict?.Select(kv => new LocalizedString(kv.Key, kv.Value)) ?? [];
    }

    // Private methods

    private static string GetString(Language language, string name, out bool found)
    {
        if (GetTranslations(language) is { } dict && dict.TryGetValue(name, out var value)) {
            found = true;
            return value;
        }

        if (language != Languages.Main
            && GetTranslations(Languages.Main) is { } mainDict
            && mainDict.TryGetValue(name, out value)) {
            found = true;
            return value;
        }

        found = false;
        return name;
    }

    // Loaded per language rather than all at once: a process renders in one or two of them,
    // and parsing every catalog would put ~60 of them on the startup path.
    private static Dictionary<string, string>? GetTranslations(Language language)
        => Translations.GetOrAdd(language, static x => Load(x));

    private static Dictionary<string, string>? Load(Language language)
    {
        // A newer device may have chosen a Preview language: its catalog ships too
        if (!Languages.AllWithCatalog.Contains(language))
            return null;

        var strings = StringCatalogs.LoadStrings(language);
        if (strings == null)
            return null;

        var messages = StringCatalogs.LoadMessages(language);
        if (messages != null)
            foreach (var (key, value) in messages)
                strings[key] = value;
        return strings;
    }
}
