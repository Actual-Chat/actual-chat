namespace ActualChat;

/// <summary>
/// This is a safety wrapper for CancellationTokenSource, which ensures
/// that CancellationTokenSource gets cancelled no matter what on disposal.
/// </summary>
public readonly struct CancellationSource : IDisposable, IEquatable<CancellationSource>, ICanBeNone<CancellationSource>
{
    private readonly CancellationTokenSource? _source;

    public static CancellationSource None { get; } = default;

    public CancellationToken Token { get; }
    public bool IsCancellationRequested => _source?.IsCancellationRequested ?? false;

    public bool IsNone => _source == null;

    public static CancellationSource New()
        => new(new CancellationTokenSource());
    public static CancellationSource New(double? timeout)
        => New().CancelAfter(timeout);
    public static CancellationSource New(double timeout)
        => New().CancelAfter(timeout);
    public static CancellationSource New(TimeSpan? timeout)
        => New().CancelAfter(timeout);
    public static CancellationSource New(TimeSpan timeout)
        => New().CancelAfter(timeout);

    public static CancellationSource NewLinked(CancellationToken cancellationToken, double? timeout)
        => new CancellationSource(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)).CancelAfter(timeout);
    public static CancellationSource NewLinked(CancellationToken cancellationToken, double timeout)
        => new CancellationSource(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)).CancelAfter(timeout);
    public static CancellationSource NewLinked(CancellationToken cancellationToken, TimeSpan? timeout)
        => new CancellationSource(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)).CancelAfter(timeout);
    public static CancellationSource NewLinked(CancellationToken cancellationToken, TimeSpan timeout)
        => new CancellationSource(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)).CancelAfter(timeout);
    public static CancellationSource NewLinked(CancellationToken cancellationToken)
        => new(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));

    public CancellationSource(CancellationTokenSource? source)
    {
        _source = source;
        Token = source?.Token ?? CancellationToken.None;
    }

    public override string ToString()
        => $"{nameof(CancellationSource)}({(IsCancellationRequested ? "cancelled" : "")})";

    public void Dispose()
        => _source.CancelAndDisposeSilently();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Cancel()
        => Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancellationSource NewLinked()
        => NewLinked(Token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancellationSource CancelAfter(double timeout)
        => CancelAfter(TimeSpan.FromSeconds(timeout));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancellationSource CancelAfter(double? timeout)
        => timeout is { } vTimeout ? CancelAfter(TimeSpan.FromSeconds(vTimeout)) : this;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancellationSource CancelAfter(TimeSpan? timeout)
        => timeout is { } vTimeout ? CancelAfter(vTimeout) : this;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancellationSource CancelAfter(TimeSpan timeout)
    {
        _source?.CancelAfter(timeout);
        return this;
    }

    // Operators

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator CancellationToken(CancellationSource source) => source.Token;

    // Equality

    public bool Equals(CancellationSource other) => Equals(_source, other._source);
    public override bool Equals(object? obj) => obj is CancellationSource other && Equals(other);
    public override int GetHashCode() => _source?.GetHashCode() ?? 0;
    public static bool operator ==(CancellationSource left, CancellationSource right) => left.Equals(right);
    public static bool operator !=(CancellationSource left, CancellationSource right) => !left.Equals(right);
}
