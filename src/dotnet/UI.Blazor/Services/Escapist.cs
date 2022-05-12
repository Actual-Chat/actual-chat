namespace ActualChat.UI.Blazor.Services;

public sealed class Escapist
{
    private readonly Func<EscapistSubscription> _factory;

    public Escapist(Func<EscapistSubscription> factory)
        => _factory = factory;

    public async Task<IAsyncDisposable> SubscribeAsync(Action action, CancellationToken token)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        var handler = _factory();
        return await handler.SubscribeAsync(action, token).ConfigureAwait(false);
    }
}
