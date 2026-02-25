namespace ActualChat.Compliance;

/// <summary>
/// A wrapper that masks its <see cref="Source"/> via <see cref="Sanitizer.Mask"/>
/// when <see cref="Sanitizer"/> is active. Pass as a log argument
/// to automatically redact sensitive data in production.
/// </summary>
public readonly struct Sensitive(object? source) : ISensitive, IEquatable<Sensitive>
{
    public readonly object? Source = source;

    public override string ToString()
        => Sanitizer.Mask(Source);

    // Equality
    public bool Equals(Sensitive other) => Equals(Source, other.Source);
    public override bool Equals(object? obj) => obj is Sensitive other && Equals(other);
    public override int GetHashCode() => Source != null ? Source.GetHashCode() : 0;
    public static bool operator ==(Sensitive left, Sensitive right) => left.Equals(right);
    public static bool operator !=(Sensitive left, Sensitive right) => !left.Equals(right);

    // Nested types

    /// <summary>
    /// A wrapper that masks its <see cref="Source"/> via <see cref="Sanitizer.Mask"/>
    /// when <see cref="Sanitizer"/> is active. Pass as a log argument
    /// to automatically redact sensitive data in production.
    /// </summary>
    public readonly struct Private(string? source) : ISensitive, IEquatable<Private>
    {
        public readonly string? Source = source;

        public override string ToString()
            => Sanitizer.MaskPrivate(Source);

        // Equality
        public bool Equals(Private other) => OrdinalEquals(Source, other.Source);
        public override bool Equals(object? obj) => obj is Private other && OrdinalEquals(Source, other.Source);
        public override int GetHashCode() => Source != null ? Source.GetOrdinalHashCode() : 0;
        public static bool operator ==(Private left, Private right) => left.Equals(right);
        public static bool operator !=(Private left, Private right) => !left.Equals(right);
    }
}
