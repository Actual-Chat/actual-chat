namespace ActualChat;

// A thread-safe version of bool? type
public sealed class BoolOption : IEquatable<BoolOption>
{
    private volatile int _value;

    public bool? Value {
        get {
            var value = _value;
            return value < 0 ? false : value > 0 ? true : null;
        }
        set {
            var v = value is { } b ? b ? 1 : -1 : 0;
#pragma warning disable CS0420
            Interlocked.Exchange(ref _value, v);
#pragma warning restore CS0420
        }
    }

    public BoolOption() => Value = null;
    public BoolOption(bool? value) => Value = value;

    // Equality

    public bool Equals(BoolOption? other) => other != null && Value == other.Value;
    public override bool Equals(object? obj) => ReferenceEquals(this, obj) || (obj is BoolOption other && Equals(other));
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(BoolOption? left, BoolOption? right) => Equals(left, right);
    public static bool operator !=(BoolOption? left, BoolOption? right) => !Equals(left, right);
}
