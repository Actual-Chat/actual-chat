using System.Text.Json;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Resources;

/// <summary>
/// <see cref="IStringLocalizer{T}"/> that reads translations from embedded JSON resources and
/// selects the language via <see cref="LanguageUI"/> — the standard .resx/CultureInfo
/// localizer doesn't work under InvariantGlobalization.
/// </summary>
public sealed class AppStringLocalizer(IServiceProvider services) : IStringLocalizer<Strings>
{
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = LoadAll();

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

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) {
        var dict = Translations.GetValueOrDefault(LanguageUI.UILanguage)
            ?? Translations.GetValueOrDefault(LanguageUI.DefaultUILanguage);
        return dict?.Select(kv => new LocalizedString(kv.Key, kv.Value)) ?? [];
    }

    // Private methods

    private string GetString(string name, out bool found) {
        var lang = LanguageUI.UILanguage;
        if (Translations.TryGetValue(lang, out var dict) && dict.TryGetValue(name, out var value)) {
            // TODO: log warning
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

    private static Dictionary<string, Dictionary<string, string>> LoadAll() {
        var result = new Dictionary<string, Dictionary<string, string>>();
        var assembly = typeof(AppStringLocalizer).Assembly;
        foreach (var lang in LanguageUI.SupportedUILanguages) {
            using var stream = assembly.GetManifestResourceStream($"Strings.{lang}.json");
            if (stream == null)
                continue;

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (dict != null)
                result[lang] = dict;
        }
        return result;
    }
}
