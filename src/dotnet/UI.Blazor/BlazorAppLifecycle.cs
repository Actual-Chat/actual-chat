namespace ActualChat.UI.Blazor;

public class BlazorAppLifecycle
{
    private readonly CancellationTokenSource _cancellationTokenSource;

    public CancellationToken StopToken { get; }

    public BlazorAppLifecycle()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        StopToken = _cancellationTokenSource.Token;
    }

    public void OnRootDisposing()
        => _cancellationTokenSource.Cancel();
}
