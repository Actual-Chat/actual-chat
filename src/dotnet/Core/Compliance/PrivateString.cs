namespace ActualChat.Compliance;

/// <summary>
/// A wrapper that masks its <see cref="Source"/> via <see cref="Sanitizer.MaskPrivate(string?)"/>
/// when <see cref="Sanitizer"/> is active. Pass as a log argument
/// to automatically redact sensitive data in production.
/// </summary>
public readonly struct PrivateString(string source)
    : ISanitized, IEquatable<PrivateString>
{
    public string Source {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => field ?? "";
    } = source;

    public override string ToString()
        => Sanitizer.MaskPrivate(Source);

    // Equality
    public bool Equals(PrivateString other) => OrdinalEquals(Source, other.Source);
    public override bool Equals(object? obj) => obj is PrivateString other && OrdinalEquals(Source, other.Source);
    public override int GetHashCode() => Source.GetOrdinalHashCode();
    public static bool operator ==(PrivateString left, PrivateString right) => left.Equals(right);
    public static bool operator !=(PrivateString left, PrivateString right) => !left.Equals(right);
}
