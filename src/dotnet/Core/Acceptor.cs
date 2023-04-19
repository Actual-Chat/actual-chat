namespace ActualChat;

public interface IHasAcceptor<TValue>
{
    Acceptor<TValue> Acceptor { get; }
}

public readonly struct Acceptor<TValue>
{
    private readonly TaskCompletionSource<TValue> _whenAcceptedSource;

    public TValue Value {
#pragma warning disable VSTHRD002
        get {
            var whenAccepted = _whenAcceptedSource.Task;
            return whenAccepted.IsCompletedSuccessfully
                ? whenAccepted.Result
                : throw StandardError.Internal("No value has been provided yet.");
        }
#pragma warning restore VSTHRD002
    }

    public Acceptor(bool runContinuationsConcurrently)
        => _whenAcceptedSource = TaskCompletionSourceExt.New<TValue>(runContinuationsConcurrently);

    public void Accept(TValue value)
    {
        var hasAccepted = _whenAcceptedSource.Task.IsCompletedSuccessfully;
        if (hasAccepted && !EqualityComparer<TValue>.Default.Equals(value, Value))
            throw StandardError.Internal("Another value has been provided already.");

        _whenAcceptedSource.TrySetResult(value);
    }

    public Task WhenAccepted(CancellationToken cancellationToken = default)
        => _whenAcceptedSource.Task.WaitAsync(cancellationToken);
}
