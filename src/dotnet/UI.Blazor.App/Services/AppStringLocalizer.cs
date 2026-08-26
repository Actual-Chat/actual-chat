using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// <see cref="IStringLocalizer{T}"/> resolving <see cref="StringCatalog"/> against
/// <see cref="LocalizationUI"/>'s language — .resx/CultureInfo can't work under InvariantGlobalization.
/// </summary>
public sealed class AppStringLocalizer(IServiceProvider services) : IStringLocalizer<Strings>, IHasUILanguage
{
    private LocalizationUI LocalizationUI => field ??= services.GetRequiredService<LocalizationUI>();

    public Language UILanguage
        // Read per lookup: this is resolved before PrepareFirstRender sets the language.
        => LocalizationUI.Language;

    public LocalizedString this[string name] => StringCatalog.Get(UILanguage, name);
    public LocalizedString this[string name, params object[] arguments]
        => StringCatalog.Get(UILanguage, name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => StringCatalog.GetAll(UILanguage);
}
