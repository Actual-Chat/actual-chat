using System.Buffers.Binary;
using System.Text;

namespace ActualChat.Transcription;

// Minimal RIFF/WAVE writer for 16-bit PCM - packages a decoded voice sample for
// SonioxVoicesClient.Create, which expects a WAV file.
public static class WavWriter
{
    private const short BitsPerSample = 16;
    private const short FormatPcm = 1;

    public static void Write(Stream stream, ReadOnlySpan<byte> pcm, int sampleRate = 48_000, short channels = 1)
    {
        var blockAlign = (short)(channels * (BitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        Span<byte> header = stackalloc byte[44];
        WriteAscii(header[..4], "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 36 + pcm.Length);
        WriteAscii(header[8..12], "WAVE");
        WriteAscii(header[12..16], "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..22], FormatPcm);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..24], channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..32], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..34], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..36], BitsPerSample);
        WriteAscii(header[36..40], "data");
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], pcm.Length);
        stream.Write(header);
        stream.Write(pcm);
    }

    private static void WriteAscii(Span<byte> destination, string ascii)
        => Encoding.ASCII.GetBytes(ascii, destination);
}
