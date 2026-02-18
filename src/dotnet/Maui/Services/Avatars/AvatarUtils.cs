namespace ActualChat.Maui.Services;

/// <summary>
/// Utility methods for deterministic avatar generation from string keys.
/// Duplicating logic from avatar-utils.ts
/// </summary>
public static class AvatarUtils
{
    public static int HashCode(string name)
    {
        var hash = 0;
        for (var i = 0; i < name.Length; i++) {
            int character = name[i];
            hash = (hash << 5) - hash + character;
            hash &= hash; // Convert to 32bit integer
        }
        return Math.Abs(hash);
    }

    public static int Mod(int num, int max)
        => num % max;

    public static int GetDigit(long number, int index)
        => (int)(Math.Floor(number / Math.Pow(10, index)) % 10);

    public static bool GetBoolDigit(int number, int index)
        => (GetDigit(number, index) % 2) == 0;

    public static double GetAngle(double x, double y)
        => Math.Atan2(y, x) * 180 / Math.PI;

    public static int GetUnit(long number, int range, int? index = null)
    {
        var value = (int)(number % range);
        return index.HasValue && GetDigit(number, index.Value) % 2 == 0 ? -value : value;
    }

    public static string GetRandomColor(int number, string[] colors, int range)
        => colors[number % range];

    public static string GetContrast(string hexColor)
    {
        // If a leading # is provided, remove it
        if (hexColor.OrdinalStartsWith("#"))
            hexColor = hexColor[1..];

        // Convert to RGB value
        var r = Convert.ToInt32(hexColor[..2], 16);
        var g = Convert.ToInt32(hexColor[2..4], 16);
        var b = Convert.ToInt32(hexColor[4..6], 16);

        // Get YIQ ratio
        var yiq = ((r * 299) + (g * 587) + (b * 114)) / 1000.0;

        // Check contrast
        return yiq >= 128 ? "000000" : "FFFFFF";
    }
}
