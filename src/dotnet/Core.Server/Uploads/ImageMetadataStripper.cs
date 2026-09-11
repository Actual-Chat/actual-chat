using System.Buffers.Binary;

namespace ActualChat.Uploads;

/// <summary>
/// Removes EXIF, XMP, IPTC and text metadata from JPEG, PNG and WebP files without touching image data.
/// Same rules as <c>src/nodejs/src/image-processing/metadata-stripper.ts</c>.
/// </summary>
public static class ImageMetadataStripper
{
    private const ushort ExifOrientationTag = 0x0112;
    private const byte WebpMetadataFlags = 0x08 | 0x04;

    private static ReadOnlySpan<byte> ExifHeader => "Exif\0\0"u8;
    private static ReadOnlySpan<byte> XmpHeader => "http://ns.adobe.com/xap/1.0/\0"u8;
    private static ReadOnlySpan<byte> HdrGainMapNamespace => "hdrgm"u8;
    private static ReadOnlySpan<byte> MpfHeader => "MPF\0"u8;

    public static byte[] Strip(byte[] data)
        // Returns data itself when nothing changes, the format isn't JPEG/PNG/WebP, or the file is malformed
        => BlobContentTypeDetector.Detect(data) switch {
            "image/jpeg" => StripJpeg(data),
            "image/png" => StripPng(data),
            "image/webp" => StripWebp(data),
            _ => data,
        };

    // Private methods

    private static byte[] StripJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2);
        var isChanged = false;
        var isAfterMpf = false;
        var offset = 2;
        while (offset + 4 <= data.Length) {
            if (data[offset] != 0xFF)
                return data;

            var marker = data[offset + 1];
            if (marker is 0xDA or 0xD9) {
                output.Write(data, offset, data.Length - offset);
                return isChanged ? output.ToArray() : data;
            }
            if (marker is 0xFF or 0x01 or >= 0xD0 and <= 0xD7) {
                var markerLength = marker == 0xFF ? 1 : 2;
                output.Write(data, offset, markerLength);
                offset += markerLength;
                continue;
            }

            var end = offset + 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            // A length field below 2 makes the segment shorter than its own header
            if (end > data.Length || end - offset < 4)
                return data;

            var segment = data.AsSpan(offset, end - offset);
            var payload = segment[4..];
            // MPF stores offsets to the images after it, so no segment behind MPF may move
            var orientation = isAfterMpf ? (ushort)0 : GetReplacementOrientation(marker, payload);
            if (marker == 0xE2 && payload.StartsWith(MpfHeader))
                isAfterMpf = true;
            if (orientation == 0)
                output.Write(segment);
            else if (orientation == 1)
                isChanged = true;
            else {
                // An already-minimal Orientation-only segment is kept as-is, so re-stripping
                // an already-stripped photo returns the same instance
                var minimal = CreateOrientationExifSegment(orientation);
                if (segment.SequenceEqual(minimal))
                    output.Write(segment);
                else {
                    isChanged = true;
                    output.Write(minimal);
                }
            }
            offset = end;
        }
        return data;
    }

    private static ushort GetReplacementOrientation(byte marker, ReadOnlySpan<byte> payload)
    {
        // 0 = keep the segment, 1 = drop it, 2..8 = replace it with an Orientation-only EXIF segment
        if (marker is 0xED or 0xFE)
            return 1;
        if (marker != 0xE1)
            return 0;
        if (payload.StartsWith(ExifHeader))
            return ReadExifOrientation(payload[ExifHeader.Length..]);

        var isHdrXmp = payload.StartsWith(XmpHeader) && payload.IndexOf(HdrGainMapNamespace) >= 0;
        return isHdrXmp ? (ushort)0 : (ushort)1;
    }

    private static ushort ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;

        var isLittleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;
        if (!isLittleEndian && !(tiff[0] == 0x4D && tiff[1] == 0x4D))
            return 1;

        var ifdOffset = isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..])
            : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifdOffset + 2L > tiff.Length)
            return 1;

        var entryCount = ReadUInt16(tiff[(int)ifdOffset..], isLittleEndian);
        for (var i = 0; i < entryCount; i++) {
            var entry = (int)ifdOffset + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
                break;
            if (ReadUInt16(tiff[entry..], isLittleEndian) != ExifOrientationTag)
                continue;

            var value = ReadUInt16(tiff[(entry + 8)..], isLittleEndian);
            return value is >= 1 and <= 8 ? value : (ushort)1;
        }
        return 1;
    }

    private static byte[] CreateOrientationExifSegment(ushort orientation)
        // Big-endian TIFF, IFD0 with one entry: Orientation, SHORT, count 1
        => [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

    private static byte[] StripPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var isChanged = false;
        var offset = 8;
        while (offset + 12 <= data.Length) {
            var end = offset + 12L + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            if (end > data.Length)
                return data;

            var type = data.AsSpan(offset + 4, 4);
            if (IsDroppedPngChunk(type))
                isChanged = true;
            else
                output.Write(data, offset, (int)end - offset);
            offset = (int)end;
            if (type.SequenceEqual("IEND"u8))
                break;
        }
        return isChanged ? output.ToArray() : data;
    }

    private static byte[] StripWebp(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 12);
        var isChanged = false;
        var offset = 12;
        while (offset + 8 <= data.Length) {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            var end = offset + 8L + size + (size & 1);
            if (end > data.Length)
                return data;

            var fourCC = data.AsSpan(offset, 4);
            if (fourCC.SequenceEqual("EXIF"u8) || fourCC.SequenceEqual("XMP "u8))
                isChanged = true;
            else
                output.Write(data, offset, (int)end - offset);
            offset = (int)end;
        }
        if (!isChanged)
            return data;

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        if (result.Length > 20 && result.AsSpan(12, 4).SequenceEqual("VP8X"u8))
            result[20] &= unchecked((byte)~WebpMetadataFlags);
        return result;
    }

    private static bool IsDroppedPngChunk(ReadOnlySpan<byte> type)
        => type.SequenceEqual("eXIf"u8)
            || type.SequenceEqual("tEXt"u8)
            || type.SequenceEqual("iTXt"u8)
            || type.SequenceEqual("zTXt"u8)
            || type.SequenceEqual("tIME"u8);

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool isLittleEndian)
        => isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
}
