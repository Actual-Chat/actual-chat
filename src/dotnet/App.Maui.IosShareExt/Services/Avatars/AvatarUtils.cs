namespace ActualChat.App.Maui.IosShareExt.Services;

public static class AvatarUtils
{
    public static int HashCode(string name)
    {
        int hash = 0;
        for (int i = 0; i < name.Length; i++)
        {
            int character = name[i];
            hash = ((hash << 5) - hash) + character;
            hash = hash & hash; // Convert to 32bit integer
        }
        return Math.Abs(hash);
    }

    public static int Mod(int num, int max)
    {
        return num % max;
    }

    public static int GetDigit(long number, int index)
    {
        return (int)(Math.Floor((double)number / Math.Pow(10, index)) % 10);
    }

    public static bool GetBoolDigit(int number, int index)
    {
        return (GetDigit(number, index) % 2) == 0;
    }

    public static double GetAngle(double x, double y)
    {
        return Math.Atan2(y, x) * 180 / Math.PI;
    }

    public static int GetUnit(long number, int range, int? index = null)
    {
        int value = (int)(number % range);

        if (index.HasValue && (GetDigit(number, index.Value) % 2) == 0)
        {
            return -value;
        }
        else
        {
            return value;
        }
    }

    public static string GetRandomColor(int number, string[] colors, int range)
    {
        return colors[number % range];
    }

    public static string GetContrast(string hexColor)
    {
        // If a leading # is provided, remove it
        if (hexColor.OrdinalStartsWith("#"))
        {
            hexColor = hexColor.Substring(1);
        }

        // Convert to RGB value
        int r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
        int g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
        int b = Convert.ToInt32(hexColor.Substring(4, 2), 16);

        // Get YIQ ratio
        double yiq = ((r * 299) + (g * 587) + (b * 114)) / 1000.0;

        // Check contrast
        return yiq >= 128 ? "000000" : "FFFFFF";
    }
}
