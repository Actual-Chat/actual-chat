namespace ActualChat.UI.Blazor.Services;

public sealed class Escapist
{
    private readonly Func<EscapistSubscription> _subscriptionFactory;

    public Escapist(Func<EscapistSubscription> subscriptionFactory)
        => _subscriptionFactory = subscriptionFactory;

    public async Task<IAsyncDisposable> SubscribeAsync(Action action, CancellationToken token)
    {
        var subscription = _subscriptionFactory();
        return await subscription.SubscribeAsync(action, token).ConfigureAwait(false);
    }
}
