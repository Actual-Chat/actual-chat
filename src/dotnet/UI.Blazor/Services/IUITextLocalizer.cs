namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Localizes an arbitrary English message at runtime — the fallback for text that isn't in
/// the string catalog. Implemented by LocalizationUI in UI.Blazor.App, which is a layer this
/// project cannot reference.
/// </summary>
public interface IUITextLocalizer : IHasServices
{
    Task<string> Get(string message, CancellationToken cancellationToken = default);
}
