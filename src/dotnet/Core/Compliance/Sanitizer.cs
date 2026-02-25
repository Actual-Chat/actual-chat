namespace ActualChat.Compliance;

#pragma warning disable CA1822

/// <summary>
/// Thread-static sanitizer that masks sensitive data when activated.
/// Call <see cref="Activate"/> to enable masking for the current thread;
/// the returned <see cref="IDisposable"/> restores the previous state.
/// </summary>
public sealed class Sanitizer
{
    private static readonly string[] LengthRanges = BuildLengthRanges();

    [ThreadStatic]
    private static bool _isActive;

    public static bool IsActive {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isActive;
    }

    public static Scope Activate()
        => new();

    public static string Mask(object? source)
        => source is null || !IsActive
            ? source?.ToString() ?? ""
            : source switch {
                string s => MaskPrivate(s),
                Sensitive.Private p => MaskPrivate(p.Source),
                ISensitive sensitive => sensitive.ToString() ?? "",
                _ => source.ToString() ?? "",
            };

    public static string MaskPrivate(string? value)
    {
        if (value.IsNullOrEmpty() || !IsActive)
            return value ?? "";

        var length = value.Length;
        return length <= 3
            ? $"<<* {GetLengthRange(length)}>>"
            : $"<<{value[0]}{value[1]}* {GetLengthRange(length)}>>";
    }

    // Private methods

    // Returns "[1-3]", "[4-7]", "[8-15]", "[16-31]", etc.
    private static string GetLengthRange(int length)
        => length < LengthRanges.Length ? LengthRanges[length] : BuildLengthRange(length);

    private static string BuildLengthRange(int length)
    {
        var highBit = 1;
        while (highBit <= length)
            highBit <<= 1;
        return $"[{highBit >> 1}-{highBit - 1}]";
    }

    private static string[] BuildLengthRanges()
    {
        // Pre-generate for lengths 0..4095
        const int size = 4096;
        var result = new string[size];
        result[0] = "[0]";
        var lo = 1;
        while (lo < size) {
            var hi = Math.Min(lo * 2 - 1, size - 1);
            var label = lo == hi ? $"[{lo}]" : $"[{lo}-{hi}]";
            for (var i = lo; i <= hi; i++)
                result[i] = label;
            lo = hi + 1;
        }
        return result;
    }

    // Nested types

    public readonly struct Scope : IDisposable
    {
        private readonly bool _wasActive;

        public Scope()
        {
            _wasActive = _isActive;
            _isActive = true;
        }

        public void Dispose()
            => _isActive = _wasActive;
    }
}
