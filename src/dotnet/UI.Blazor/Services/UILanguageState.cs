namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// The UI language of the current circuit, used to resolve .resx resources.
/// </summary>
public sealed class UILanguageState
{
    public string IsoCode { get; set; } = "en";
}
