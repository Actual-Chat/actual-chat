namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct MaybeTrimmed<T>(
    T Value,
    bool IsTrimmed = false)
{
    public void Deconstruct(out T value, out bool isTrimmed)
    {
        value = Value;
        isTrimmed = IsTrimmed;
    }

    public override string ToString()
        => Format();
    public string Format(string trimmedSuffix = "+")
        => Invariant($"{Value?.ToString()}{(IsTrimmed ? trimmedSuffix : "")}");

    public static implicit operator MaybeTrimmed<T>(T value) => new(value);
    public static implicit operator MaybeTrimmed<T>((T Value, bool IsTrimmed) source)
        => new(source.Value, source.IsTrimmed);
}
