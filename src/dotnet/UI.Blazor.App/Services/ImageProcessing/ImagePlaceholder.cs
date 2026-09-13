namespace ActualChat.UI.Blazor.App.Services;

public static class ImagePlaceholder
{
    private const byte FormatStripped = 1;
    private const byte FormatFull = 2;
    private const int LongSide = 64;
    private const string DataUrlPrefix = "data:image/jpeg;base64,";

    // Must stay byte-identical to PLACEHOLDER_PREFIX in placeholder-encoder.ts;
    // a change there needs a new format mark, not an edit here
    private static readonly byte[] Prefix = [
        255, 216, 255, 219, 0, 197, 0, 16, 11, 11, 24, 17, 24, 25, 24, 24,
        25, 43, 29, 30, 29, 43, 44, 44, 34, 34, 44, 44, 49, 40, 44, 43,
        44, 40, 49, 49, 51, 50, 53, 53, 50, 51, 49, 51, 48, 54, 58, 54,
        48, 51, 54, 54, 63, 63, 54, 54, 59, 71, 69, 71, 59, 70, 65, 65,
        70, 78, 69, 78, 69, 69, 56, 1, 15, 13, 13, 26, 22, 26, 32, 26,
        26, 32, 44, 41, 41, 41, 44, 62, 64, 60, 60, 64, 62, 76, 66, 65,
        72, 65, 66, 76, 124, 141, 115, 72, 72, 115, 141, 124, 94, 255, 96, 77,
        96, 255, 94, 108, 112, 63, 63, 112, 108, 175, 69, 128, 69, 175, 250, 189,
        189, 250, 62, 62, 62, 62, 62, 255, 2, 15, 9, 9, 21, 16, 21, 19,
        20, 20, 19, 34, 21, 25, 21, 34, 32, 31, 23, 23, 31, 32, 38, 28,
        29, 27, 29, 28, 38, 41, 35, 33, 35, 35, 33, 35, 41, 37, 34, 30,
        27, 30, 34, 37, 39, 33, 41, 41, 33, 39, 41, 31, 35, 31, 41, 39,
        40, 40, 39, 59, 64, 59, 59, 59, 255, 255, 192, 0, 17, 8, 0, 48,
        0, 64, 3, 1, 34, 0, 2, 17, 1, 3, 17, 2,
    ];
    private static readonly byte[] Eoi = [0xFF, 0xD9];
    private static readonly int SofHeightOffset = FindSofDimensionOffset(Prefix);

    public static string ToDataUrl(string? packedBase64)
    {
        if (packedBase64.IsNullOrEmpty())
            return "";

        byte[] packed;
        try {
            packed = Convert.FromBase64String(packedBase64);
        }
        catch (FormatException) {
            return "";
        }
        if (packed.Length < 3)
            return "";

        var payload = packed.AsSpan(2);
        if (packed[0] == FormatFull)
            return DataUrlPrefix + Convert.ToBase64String(payload);
        if (packed[0] != FormatStripped)
            return "";

        var shortSide = unchecked((sbyte)packed[1]);
        var width = shortSide > 0 ? shortSide : LongSide;
        var height = shortSide > 0 ? LongSide : -shortSide;
        var jpeg = new byte[Prefix.Length + payload.Length + Eoi.Length];
        Prefix.CopyTo(jpeg, 0);
        payload.CopyTo(jpeg.AsSpan(Prefix.Length));
        Eoi.CopyTo(jpeg.AsSpan(Prefix.Length + payload.Length));
        WriteBigEndian(jpeg, SofHeightOffset, (ushort)height);
        WriteBigEndian(jpeg, SofHeightOffset + 2, (ushort)width);
        return DataUrlPrefix + Convert.ToBase64String(jpeg);
    }

    // Private methods

    private static int FindSofDimensionOffset(byte[] jpeg)
    {
        // Walks segments to the SOF0 marker (0xFFC0) and returns the offset of its
        // height field, five bytes past the marker start.
        var offset = 2;
        while (offset + 4 <= jpeg.Length) {
            var marker = jpeg[offset + 1];
            if (marker == 0xC0)
                return offset + 5;

            var segmentLength = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
            offset += 2 + segmentLength;
        }
        throw new InvalidOperationException("ImagePlaceholder: prefix has no SOF0 marker");
    }

    private static void WriteBigEndian(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }
}
