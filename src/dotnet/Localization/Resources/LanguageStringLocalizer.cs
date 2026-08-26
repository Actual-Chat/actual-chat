using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// An <see cref="IStringLocalizer"/> bound to an explicit language, for code with no circuit.
/// </summary>
public sealed class LanguageStringLocalizer : IStringLocalizer, IHasUILanguage
{
    private static readonly ConcurrentDictionary<Language, LanguageStringLocalizer> Cache = new();

    public Language UILanguage { get; }

    public static LanguageStringLocalizer Get(Language language)
        => Cache.GetOrAdd(language, static x => new LanguageStringLocalizer(x));

    private LanguageStringLocalizer(Language language)
        => UILanguage = language;

    public LocalizedString this[string name] => StringCatalog.Get(UILanguage, name);
    public LocalizedString this[string name, params object[] arguments]
        => StringCatalog.Get(UILanguage, name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => StringCatalog.GetAll(UILanguage);
}
