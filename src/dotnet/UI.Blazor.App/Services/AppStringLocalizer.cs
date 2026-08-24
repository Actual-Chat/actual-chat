using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// <see cref="IStringLocalizer{T}"/> that reads translations from embedded JSON resources and
/// selects the language via <see cref="LocalizationUI"/> — the standard .resx/CultureInfo
/// localizer doesn't work under InvariantGlobalization.
/// </summary>
public sealed class AppStringLocalizer(IServiceProvider services) : IStringLocalizer<Strings>, IHasUILanguage
{
    private static readonly Dictionary<Language, Dictionary<string, string>> Translations = LoadAll();

    private LocalizationUI LocalizationUI => field ??= services.GetRequiredService<LocalizationUI>();

    public Language UILanguage => LocalizationUI.Language;

    public LocalizedString this[string name] {
        get {
            var value = GetString(name, out var found);
            return new LocalizedString(name, value, resourceNotFound: !found);
        }
    }

    public LocalizedString this[string name, params object[] arguments] {
        get {
            var value = GetString(name, out var found);
            var formatted = found ? string.Format(value, arguments) : value;
            return new LocalizedString(name, formatted, resourceNotFound: !found);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var dict = Translations.GetValueOrDefault(LocalizationUI.Language)
            ?? Translations.GetValueOrDefault(Languages.Main);
        return dict?.Select(kv => new LocalizedString(kv.Key, kv.Value)) ?? [];
    }

    // Private methods

    private string GetString(string name, out bool found)
    {
        var lang = LocalizationUI.Language;
        if (Translations.TryGetValue(lang, out var dict) && dict.TryGetValue(name, out var value)) {
            found = true;
            return value;
        }

        if (lang != Languages.Main
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
        foreach (var lang in Languages.AllUI) {
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
