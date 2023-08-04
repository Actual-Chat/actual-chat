namespace ActualChat.UI.Blazor.Services;

public sealed class Escapist
{
    private IJSRuntime JS { get; }

    public Escapist(IJSRuntime js)
        => JS = js;

    public ValueTask<IAsyncDisposable> Subscribe(Action action, CancellationToken cancellationToken = default)
        => EscapistSubscription.Create(JS, action, once: false, cancellationToken);

    public ValueTask<IAsyncDisposable> SubscribeOnce(Action action, CancellationToken cancellationToken = default)
        => EscapistSubscription.Create(JS, action, once: true, cancellationToken);
}
