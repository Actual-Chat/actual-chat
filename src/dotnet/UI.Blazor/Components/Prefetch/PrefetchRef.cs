namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// What a <c>data-prefetch</c> attribute carries: the <see cref="IPrefetcher"/> to run and its
/// arguments. Rendered into markup, read back by the document-level pointer-down handler.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PrefetchRef(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type prefetcherType,
    params string[] arguments)
{
    private const char Delimiter = '|';

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type PrefetcherType { get; } = prefetcherType;
    public string[] Arguments { get; } = arguments;

    public static PrefetchRef New<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPrefetcher>(params string[] arguments)
        where TPrefetcher : IPrefetcher
        => new (typeof(TPrefetcher), arguments);

    public override string ToString()
    {
        var bufferLength = Arguments.Length + 1;
        var buffer = new RefArrayPoolBuffer<string>(ArrayPools.SharedStringPool, bufferLength, mustClear: true);
        var span = buffer.Array.AsSpan(0, bufferLength);
        try {
            span[0] = PrefetchRegistry.GetTypeId(PrefetcherType);
            for (var i = 0; i < Arguments.Length; i++)
                span[i + 1] = Arguments[i].ToBase64();
            return string.Join(Delimiter, span);
        }
        finally {
            buffer.Release();
        }
    }

    // Parse & TryParse

    public static PrefetchRef Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<PrefetchRef>();

    public static bool TryParse(string value, out PrefetchRef result)
    {
        var parts = value.Split(Delimiter);
        if (parts.Length == 0 || parts[0].IsNullOrEmpty()) {
            result = default;
            return false;
        }

        if (PrefetchRegistry.TryGetType(parts[0]) is not { } prefetcherType) {
            result = default;
            return false;
        }

        try {
            for (var i = 1; i < parts.Length; i++)
                parts[i] = parts[i].FromBase64();
        }
        catch (FormatException) {
            result = default;
            return false;
        }

        result = new PrefetchRef(prefetcherType, parts[1..]);
        return true;
    }
}
