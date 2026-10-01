using ActualChat.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace ActualChat.Users.Templates;

/// <summary>
/// Base of every email template. <see cref="L"/> is bound to the recipient's language:
/// <see cref="BlazorRenderer"/> registers the localizer it was created with.
/// </summary>
public abstract class EmailTemplateBase : ComponentBase
{
    [Inject] protected IStringLocalizer L { get; init; } = null!;

    protected string LanguageCode => ((IHasUILanguage)L).UILanguage.IsoCode;
}
