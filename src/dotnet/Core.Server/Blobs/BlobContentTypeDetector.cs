using System.Buffers;

namespace ActualChat.Blobs;

public static class BlobContentTypeDetector
{
    private const int PrefixLength = 4096;

    public static async Task<string?> Detect(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
            throw StandardError.Internal("Blob stream must be seekable.");

        var initialPosition = stream.Position;
        var buffer = ArrayPool<byte>.Shared.Rent(PrefixLength);
        try {
            var readLength = 0;
            while (readLength < PrefixLength) {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(readLength, PrefixLength - readLength), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                readLength += read;
            }
            return Detect(buffer.AsSpan(0, readLength));
        }
        finally {
            stream.Position = initialPosition;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8
            && content[0] == 0x89
            && content[1..].StartsWith("PNG\r\n\x1A\n"u8))
            return "image/png";
        if (content.Length >= 3
            && content[0] == 0xFF
            && content[1] == 0xD8
            && content[2] == 0xFF)
            return "image/jpeg";
        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
            return "image/gif";
        if (content.StartsWith("BM"u8))
            return "image/bmp";
        if (content.Length >= 4
            && content[0] == 0
            && content[1] == 0
            && content[2] == 1
            && content[3] == 0)
            return "image/vnd.microsoft.icon";
        if (content.StartsWith("II*\0"u8) || content.StartsWith("MM\0*"u8))
            return "image/tiff";
        if (content.StartsWith("%PDF-"u8))
            return "application/pdf";

        if (content.Length >= 12 && content.StartsWith("RIFF"u8)) {
            var riffType = content[8..12];
            if (riffType.SequenceEqual("WEBP"u8))
                return "image/webp";
            if (riffType.SequenceEqual("WAVE"u8))
                return "audio/wav";
            if (riffType.SequenceEqual("AVI "u8))
                return "video/avi";
        }
        if (content.Length >= 12 && content.StartsWith("FORM"u8)) {
            var formType = content[8..12];
            if (formType.SequenceEqual("AIFF"u8) || formType.SequenceEqual("AIFC"u8))
                return "audio/aiff";
        }

        if (content.Length >= 12 && content[4..8].SequenceEqual("ftyp"u8))
            return DetectIsoBaseMediaType(content);

        if (content.StartsWith("fLaC"u8))
            return "audio/flac";
        if (content.StartsWith("OpusHead"u8))
            return "audio/opus";
        if (content.StartsWith("MThd"u8))
            return "audio/midi";
        if (content.StartsWith("#!AMR\n"u8) || content.StartsWith("#!AMR-WB\n"u8))
            return "audio/amr";
        if (content.StartsWith("ID3"u8))
            return "audio/mpeg";
        if (content.Length >= 2
            && content[0] == 0xFF
            && (content[1] & 0xF6) == 0xF0)
            return "audio/aac";
        if (content.Length >= 2
            && content[0] == 0xFF
            && (content[1] & 0xE0) == 0xE0)
            return "audio/mpeg";

        if (content.StartsWith("OggS"u8)) {
            if (content.IndexOf("theora"u8) >= 0)
                return "video/ogg";
            if (content.IndexOf("OpusHead"u8) >= 0
                || content.IndexOf("vorbis"u8) >= 0
                || content.IndexOf("Speex   "u8) >= 0)
                return "audio/ogg";

            return null;
        }

        if (content.Length >= 4
            && content[0] == 0x1A
            && content[1] == 0x45
            && content[2] == 0xDF
            && content[3] == 0xA3)
            return DetectEbmlType(content);
        if (content.StartsWith("FLV"u8))
            return "video/x-flv";

        if (content.Length >= 4
            && content[0] == 0
            && content[1] == 0
            && content[2] == 1
            && content[3] is 0xBA or 0xB3)
            return "video/mpeg";
        if (LooksLikeSvg(content))
            return "image/svg+xml";

        return null;
    }

    private static string DetectIsoBaseMediaType(ReadOnlySpan<byte> content)
    {
        var brand = content[8..12];
        if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            return "image/avif";
        if (brand.SequenceEqual("heic"u8)
            || brand.SequenceEqual("heix"u8)
            || brand.SequenceEqual("hevc"u8)
            || brand.SequenceEqual("hevx"u8))
            return "image/heic";
        if (brand.SequenceEqual("heif"u8)
            || brand.SequenceEqual("heim"u8)
            || brand.SequenceEqual("heis"u8)
            || brand.SequenceEqual("mif1"u8)
            || brand.SequenceEqual("msf1"u8))
            return "image/heif";
        if (brand.SequenceEqual("M4A "u8)
            || brand.SequenceEqual("M4B "u8)
            || brand.SequenceEqual("M4P "u8))
            return "audio/mp4";
        if (brand.SequenceEqual("qt  "u8))
            return "video/quicktime";
        if (brand.StartsWith("3gp"u8) && content.IndexOf("vide"u8) < 0)
            return "audio/3gpp";
        if (brand.StartsWith("3gp"u8))
            return "video/3gpp";
        if (content.IndexOf("vide"u8) < 0 && content.IndexOf("soun"u8) >= 0)
            return "audio/mp4";

        return "video/mp4";
    }

    private static string DetectEbmlType(ReadOnlySpan<byte> content)
    {
        if (content.IndexOf("webm"u8) < 0)
            return "video/x-matroska";
        if (content.IndexOf("V_VP8"u8) < 0
            && content.IndexOf("V_VP9"u8) < 0
            && content.IndexOf("V_AV1"u8) < 0
            && (content.IndexOf("A_OPUS"u8) >= 0 || content.IndexOf("A_VORBIS"u8) >= 0))
            return "audio/webm";

        return "video/webm";
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3
            && content[0] == 0xEF
            && content[1] == 0xBB
            && content[2] == 0xBF)
            content = content[3..];
        content = TrimStart(content);

        while (content.StartsWith("<?xml"u8) || content.StartsWith("<!--"u8)) {
            var terminator = content.StartsWith("<?xml"u8) ? "?>"u8 : "-->"u8;
            var endIndex = content.IndexOf(terminator);
            if (endIndex < 0)
                return false;

            content = TrimStart(content[(endIndex + terminator.Length)..]);
        }

        return content.StartsWith("<svg"u8)
            && (content.Length == 4
                || content[4] is (byte)'>' or (byte)'/' or (byte)' '
                    or (byte)'\t' or (byte)'\r' or (byte)'\n');
    }

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> content)
    {
        var index = 0;
        while (index < content.Length && content[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            index++;

        return content[index..];
    }
}
