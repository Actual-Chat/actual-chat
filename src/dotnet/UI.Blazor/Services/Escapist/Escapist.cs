namespace ActualChat.UI.Blazor.Services;

public sealed class Escapist(IJSRuntime js)
{
    private IJSRuntime JS { get; } = js;

    public ValueTask<IAsyncDisposable> Subscribe(Action action, CancellationToken cancellationToken = default)
        => EscapistSubscription.Create(JS, action, once: false, cancellationToken);

    public ValueTask<IAsyncDisposable> SubscribeOnce(Action action, CancellationToken cancellationToken = default)
        => EscapistSubscription.Create(JS, action, once: true, cancellationToken);
}
