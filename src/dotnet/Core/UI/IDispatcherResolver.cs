using Microsoft.AspNetCore.Components;

namespace ActualChat.UI;

/// <summary>
/// Provides access to the Blazor dispatcher for UI thread synchronization.
/// </summary>
public interface IDispatcherResolver : IHasServices
{
    Task WhenInitialized { get; }
    Dispatcher Dispatcher { get; }
    CancellationToken StopToken { get; }
}
