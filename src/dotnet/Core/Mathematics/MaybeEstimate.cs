namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct MaybeEstimate<T>(
    T Value,
    bool IsEstimate = false)
{
    public override string ToString()
        => Format();
    public string Format(string estimateSuffix = "+")
        => Invariant($"{Value?.ToString()}{(IsEstimate ? estimateSuffix : "")}");

    public static implicit operator MaybeEstimate<T>(T value) => new(value);
    public static implicit operator MaybeEstimate<T>((T Value, bool IsEstimate) source)
        => new(source.Value, source.IsEstimate);
}
