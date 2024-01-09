using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CA1001 // Type 'Alphabet' owns disposable field(s) 'Generator16', 'Generator8' but is not disposable

[StructLayout(LayoutKind.Auto)]
public sealed class Alphabet
{
    private static readonly UInt128 One = new(0, 1);

    public static readonly Alphabet Numeric = "0123456789";
    public static readonly Alphabet NumericDash = "0123456789-";
    public static readonly Alphabet AlphaLower = "abcdefghijklmnopqrstuvwxyz";
    public static readonly Alphabet AlphaUpper = AlphaLower.Symbols.ToUpperInvariant();
    public static readonly Alphabet Alpha = AlphaLower + AlphaUpper;
    public static readonly Alphabet AlphaNumeric = Alpha + Numeric;
    public static readonly Alphabet AlphaNumericDash = Alpha + NumericDash;
    public static readonly Alphabet AlphaNumericLower = AlphaLower + Numeric;
    public static readonly Alphabet AlphaNumericLowerDash = AlphaLower + NumericDash;
    public static readonly Alphabet AlphaNumericUpper = AlphaUpper + Numeric;
    public static readonly Alphabet AlphaNumericUpperDash = AlphaUpper + NumericDash;
    public static readonly Alphabet AlphaNumeric64 = AlphaNumeric + "-_";
    public static readonly Alphabet Base64 = AlphaNumeric + "+/";
    public static readonly Alphabet Base32 = AlphaNumericLower.Symbols[..32];
    public static readonly Alphabet Base16 = AlphaNumericLower.Symbols[..16];

    // We use readonly to (possibly) speed things up here
    public readonly string Symbols;
    public readonly UInt128 BitMask;
    public readonly RandomStringGenerator Generator8;
    public readonly RandomStringGenerator Generator16;

    public Alphabet(string symbols)
    {
        Symbols = symbols;
        BitMask = 0;
        foreach (var c in symbols) {
            if (c >= 128)
                throw new ArgumentOutOfRangeException(nameof(symbols), "All characters must have an UTF16 code < 128.");
            var oldMask = BitMask;
            BitMask |= One << c;
            if (oldMask == BitMask)
                throw new ArgumentOutOfRangeException(nameof(symbols), "All characters must be unique.");
        }
        Generator8 = new RandomStringGenerator(8, symbols);
        Generator16 = new RandomStringGenerator(16, symbols);
    }

    // Conversion

    public override string ToString() => Symbols;

    public static implicit operator Alphabet(string source) => new(source);
    public static implicit operator string(Alphabet source) => source.Symbols;

    // Checks

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(char c) => c < 128 && (BitMask & (One << c)) != default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(in ReadOnlySpan<char> s)
    {
        foreach (var c in s)
            if (!IsMatch(c))
                return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMatch(string s)
    {
        foreach (var c in s)
            if (!IsMatch(c))
                return false;
        return true;
    }
}
