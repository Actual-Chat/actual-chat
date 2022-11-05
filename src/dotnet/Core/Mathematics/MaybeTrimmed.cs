namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
public readonly record struct MaybeTrimmed<T>(
    T Value,
    bool IsTrimmed = false)
    where T : notnull
{
    public MaybeTrimmed(T value, T maxValue) : this(value)
    {
        if (Comparer<T>.Default.Compare(value, maxValue) < 0)
            return;

        Value = maxValue;
        IsTrimmed = true;
    }

    public void Deconstruct(out T value, out bool isTrimmed)
    {
        value = Value;
        isTrimmed = IsTrimmed;
    }

    public override string ToString()
        => Format();
    public string Format(string trimmedSuffix = "+")
        => Invariant($"{Value}{(IsTrimmed ? trimmedSuffix : "")}");

    public static implicit operator MaybeTrimmed<T>(T value) => new(value);
    public static implicit operator MaybeTrimmed<T>((T Value, bool IsTrimmed) source)
        => new(source.Value, source.IsTrimmed);
    public static implicit operator MaybeTrimmed<T>((T Value, T MaxValue) source)
        => new(source.Value, source.MaxValue);
}
