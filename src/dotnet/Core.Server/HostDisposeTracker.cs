using Microsoft.Extensions.Hosting;

namespace ActualChat;

public sealed class HostDisposeTracker : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _tokenSource;
    public CancellationToken Token { get; }

    public HostDisposeTracker() : this(null) { }
    public HostDisposeTracker(IHostApplicationLifetime? hostLifetime)
    {
        _tokenSource = hostLifetime?.ApplicationStopping.CreateLinkedTokenSource() ?? new();
        Token = _tokenSource.Token;
    }

    public void Dispose()
        => _tokenSource.CancelAndDisposeSilently();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    public CancellationTokenSource NewCancellationTokenSource()
        => Token.CreateLinkedTokenSource();
}
