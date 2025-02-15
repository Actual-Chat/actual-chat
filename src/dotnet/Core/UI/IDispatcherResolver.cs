using Microsoft.AspNetCore.Components;

namespace ActualChat.UI;

public interface IDispatcherResolver : IHasServices
{
    Task WhenInitialized { get; }
    Dispatcher Dispatcher { get; }
    CancellationToken StopToken { get; }
}
