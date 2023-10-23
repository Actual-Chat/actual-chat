using Microsoft.AspNetCore.Components;

namespace ActualChat.UI;

public interface IDispatcherResolver : IHasServices
{
    Task WhenReady { get; }
    Dispatcher Dispatcher { get; }
    CancellationToken StopToken { get; }
}
