using System.Numerics;

namespace ActualChat.Mathematics;

/// <summary>
/// Math helpers for tile operations on <see cref="INumber{T}"/> types.
/// </summary>
public static class TileMath
{
    /// <summary>
    /// Divides <paramref name="a"/> by <paramref name="b"/> and returns the quotient,
    /// ensuring the remainder is non-negative.
    /// </summary>
    public static long DivNonNegativeRem<T>(T a, T b, out T remainder)
        where T : struct, INumber<T>
    {
        var q = a / b;
        var r = a - q * b;
        if (r < T.Zero) {
            q -= T.One;
            r += b;
        }
        remainder = r;
        return long.CreateChecked(q);
    }

    /// <summary>
    /// Returns the non-negative remainder of <paramref name="a"/> divided by <paramref name="b"/>.
    /// </summary>
    public static T Mod<T>(T a, T b)
        where T : struct, INumber<T>
    {
        _ = DivNonNegativeRem(a, b, out var mod);
        return mod;
    }
}
