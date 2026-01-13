namespace ActualChat;

public static class Base32Encoder
{
    private static readonly Alphabet Alphabet = Alphabet.Base32;
    private static readonly sbyte[] SymbolIndex;

    static Base32Encoder()
    {
        SymbolIndex = new sbyte[128];
        SymbolIndex.AsSpan().Fill(-1);
        for (var i = 0; i < Alphabet.Symbols.Length; i++)
            SymbolIndex[Alphabet.Symbols[i]] = (sbyte)i;
        if (SymbolIndex['0'] == -1)
            SymbolIndex['0'] = 0;
    }

    public static string Encode(uint value)
        => Encode((ulong)value);

    public static string Encode(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
        return Encode((ulong)value);
    }

    public static string Encode(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
        return Encode((ulong)value);
    }

    public static string Encode(ulong value)
    {
        if (value == 0) return "0";

        Span<char> buffer = stackalloc char[13]; // enough for ulong (64 bits / 5 bits per char = 12.8)
        int pos = buffer.Length;

        while (value > 0) {
            buffer[--pos] = Alphabet.Symbols[(int)(value & 0x1F)];
            value >>= 5;
        }

        return new string(buffer[pos..]);
    }

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return "";

        int charCount = (int)(((long)bytes.Length * 8 + 4) / 5);
        var buffer = charCount <= 256 ? stackalloc char[charCount] : new char[charCount];

        int charPos = 0;
        int bitsLeft = 0;
        uint current = 0;

        foreach (var b in bytes) {
            current = (current << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5) {
                bitsLeft -= 5;
                buffer[charPos++] = Alphabet.Symbols[(int)((current >> bitsLeft) & 0x1F)];
            }
        }

        if (bitsLeft > 0)
            buffer[charPos++] = Alphabet.Symbols[(int)((current << (5 - bitsLeft)) & 0x1F)];

        return new string(buffer[..charPos]);
    }

    public static ulong Decode(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        if (value.Equals("0", StringComparison.Ordinal)) return 0;

        ulong result = 0;
        foreach (var c in value) {
            int digit = c < 128 ? SymbolIndex[c] : -1;
            if (digit < 0)
                throw new ArgumentException($"Invalid character '{c}' in base32 string.", nameof(value));
            result = (result << 5) | (ulong)digit;
        }

        return result;
    }

    public static byte[] DecodeBytes(string value)
    {
        if (string.IsNullOrEmpty(value)) return [];

        int byteCount = value.Length * 5 / 8;
        var result = new byte[byteCount];

        int bytePos = 0;
        int bitsLeft = 0;
        uint current = 0;

        foreach (var c in value) {
            int digit = c < 128 ? SymbolIndex[c] : -1;
            if (digit < 0)
                throw new ArgumentException($"Invalid character '{c}' in base32 string.", nameof(value));

            current = (current << 5) | (uint)digit;
            bitsLeft += 5;

            if (bitsLeft >= 8) {
                bitsLeft -= 8;
                result[bytePos++] = (byte)((current >> bitsLeft) & 0xFF);
            }
        }

        return result;
    }
}
