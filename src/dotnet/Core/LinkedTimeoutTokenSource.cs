namespace ActualChat;

public sealed class LinkedTimeoutTokenSource : IDisposable
{
    private readonly CancellationTokenSource _tokenSource;
    private readonly CancellationTokenRegistration _registration;

    public CancellationToken SourceToken { get; }
    public TimeSpan? Timeout { get; }
    public TimeSpan? CancellationDelay { get; }
    public CancellationToken Token { get; }

    public LinkedTimeoutTokenSource(CancellationToken sourceToken, TimeSpan? timeout, TimeSpan? cancellationDelay)
    {
        SourceToken = sourceToken;
        Timeout = timeout;
        CancellationDelay = cancellationDelay;

        _tokenSource = timeout.HasValue
            ? new CancellationTokenSource(timeout.GetValueOrDefault())
            : new CancellationTokenSource();
        Token = _tokenSource.Token;
        _registration = sourceToken.Register(self => (self as LinkedTimeoutTokenSource)?.DelayedCancelSilently(), this);
    }

    public void Cancel()
        => _tokenSource.Cancel();

    public void CancelSilently()
    {
        try {
            Cancel();
        }
        catch {
            // Intended
        }
    }

    public void DelayedCancel()
    {
        if (CancellationDelay.HasValue)
            _tokenSource.CancelAfter(CancellationDelay.GetValueOrDefault());
        else
            _tokenSource.Cancel();
    }

    public void DelayedCancelSilently()
    {
        try {
            DelayedCancel();
        }
        catch {
            // Intended
        }
    }

    public void CancelAndDisposeSilently()
    {
        try {
            _registration.Dispose();
        }
        catch {
            // Intended
        }
        _tokenSource.CancelAndDisposeSilently();
    }

    public void Dispose()
    {
        _registration.Dispose();
        _tokenSource.Dispose();
    }
}
