namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI : IHasAcceptor<BubbleHost>
{
    private Acceptor<BubbleHost> HostAcceptor { get; } = new (true);

    Acceptor<BubbleHost> IHasAcceptor<BubbleHost>.Acceptor => HostAcceptor;

    public Task WhenReady => HostAcceptor.WhenAccepted();
    public BubbleHost Host => HostAcceptor.Value;

    public async ValueTask Show(string group, CancellationToken cancellationToken = default)
    {
        await WhenReady;

        return Host.Show(group);
    }
}
