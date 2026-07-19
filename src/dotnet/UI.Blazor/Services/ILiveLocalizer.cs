namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Localizes server-composed message text (e.g. error/exception messages that cross RPC)
/// into the device UI language via AI translation. Implemented in UI.Blazor.App.
/// </summary>
public interface ILiveLocalizer
{
    Task<string> Localize(string message, CancellationToken cancellationToken = default);
}
