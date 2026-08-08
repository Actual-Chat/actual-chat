using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// <see cref="IStringLocalizer{T}"/> that reads translations from embedded JSON resources and
/// selects the language via <see cref="LanguageUI"/> — the standard .resx/CultureInfo
/// localizer doesn't work under InvariantGlobalization.
/// </summary>
public sealed class AppStringLocalizer(IServiceProvider services) : IStringLocalizer<Strings>
{
    private static readonly Dictionary<Language, Dictionary<string, string>> Translations = LoadAll();

    private LanguageUI LanguageUI => field ??= services.GetRequiredService<LanguageUI>();

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
        var dict = Translations.GetValueOrDefault(LanguageUI.UILanguage.Value)
            ?? Translations.GetValueOrDefault(LanguageUI.DefaultUILanguage);
        return dict?.Select(kv => new LocalizedString(kv.Key, kv.Value)) ?? [];
    }

    // Private methods

    private string GetString(string name, out bool found)
    {
        var lang = LanguageUI.UILanguage.Value;
        if (Translations.TryGetValue(lang, out var dict) && dict.TryGetValue(name, out var value)) {
            found = true;
            return value;
        }

        if (lang != LanguageUI.DefaultUILanguage
            && Translations.TryGetValue(LanguageUI.DefaultUILanguage, out dict)
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
        foreach (var lang in LanguageUI.SupportedUILanguages) {
            var subtag = lang.IsoCode;
            var strings = StringCatalog.Load(StringCatalog.StringsPrefix, subtag);
            if (strings == null)
                continue;

            var messages = StringCatalog.Load(StringCatalog.MessagesPrefix, subtag);
            if (messages != null)
                foreach (var (key, value) in messages)
                    strings[key] = value;
            // TODO(FC): check again if we need to merge 2 different translation sets into a single one?
            result[lang] = strings;
        }
        return result;
    }
}
