namespace ActualChat.Localization;

/// <summary>
/// The UI language behind an <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>.
/// Implemented by the localizer, which is the only component that resolves the language -
/// nothing in UI.Blazor can reach the setting it comes from.
/// </summary>
public interface IHasUILanguage
{
    Language UILanguage { get; }
}
