using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// The merged Strings + Messages catalog for every shipped UI language, loaded once.
/// Callers pick the language; this type never resolves one.
/// </summary>
public static class StringCatalog
{
    private static readonly Dictionary<Language, Dictionary<string, string>> Translations = LoadAll();

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
        var dict = Translations.GetValueOrDefault(language)
            ?? Translations.GetValueOrDefault(Languages.Main);
        return dict?.Select(kv => new LocalizedString(kv.Key, kv.Value)) ?? [];
    }

    // Private methods

    private static string GetString(Language language, string name, out bool found)
    {
        if (Translations.TryGetValue(language, out var dict) && dict.TryGetValue(name, out var value)) {
            found = true;
            return value;
        }

        if (language != Languages.Main
            && Translations.TryGetValue(Languages.Main, out dict)
            && dict.TryGetValue(name, out value)) {
            found = true;
            return value;
        }

        found = false;
        return name;
    }

    private static Dictionary<Language, Dictionary<string, string>> LoadAll()
    {
        var result = new Dictionary<Language, Dictionary<string, string>>();
        foreach (var lang in Languages.AllUIAndTestOnly) {
            var strings = StringCatalogs.LoadStrings(lang);
            if (strings == null)
                continue;

            var messages = StringCatalogs.LoadMessages(lang);
            if (messages != null)
                foreach (var (key, value) in messages)
                    strings[key] = value;
            result[lang] = strings;
        }
        return result;
    }
}
