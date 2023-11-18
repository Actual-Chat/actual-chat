namespace ActualChat;

public interface IHasAcceptor<TValue>
{
    Acceptor<TValue> Acceptor { get; }
}

public readonly struct Acceptor<TValue>(bool runContinuationsConcurrently) : IEquatable<Acceptor<TValue>>
{
    private readonly TaskCompletionSource<TValue> _whenAcceptedSource
        = TaskCompletionSourceExt.New<TValue>(runContinuationsConcurrently);

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

    public void Accept(TValue value)
    {
        var hasAccepted = _whenAcceptedSource.Task.IsCompletedSuccessfully;
        if (hasAccepted && !EqualityComparer<TValue>.Default.Equals(value, Value))
            throw StandardError.Internal("Another value has been provided already.");

        _whenAcceptedSource.TrySetResult(value);
    }

    public Task WhenAccepted(CancellationToken cancellationToken = default)
        => _whenAcceptedSource.Task.WaitAsync(cancellationToken);

    // Equality
    public bool Equals(Acceptor<TValue> other) => _whenAcceptedSource.Equals(other._whenAcceptedSource);
    public override bool Equals(object? obj) => obj is Acceptor<TValue> other && Equals(other);
    public override int GetHashCode() => _whenAcceptedSource.GetHashCode();
    public static bool operator ==(Acceptor<TValue> left, Acceptor<TValue> right) => left.Equals(right);
    public static bool operator !=(Acceptor<TValue> left, Acceptor<TValue> right) => !left.Equals(right);
}
