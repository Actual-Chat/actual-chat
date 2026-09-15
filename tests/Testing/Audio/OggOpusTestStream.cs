using ActualChat.Audio;
using ActualChat.Audio.Ogg;

namespace ActualChat.Testing.Audio;

/// <summary>
/// Builds Ogg/Opus byte streams out of synthetic 20 ms packets for tests that stand in for Soniox TTS.
/// </summary>
public static class OggOpusTestStream
{
    // A CELT fullband 20 ms single-frame TOC (what Soniox sends) followed by a payload long enough
    // on every other frame to need 255-lacing, so packets span several lacing values; the writer
    // never continues a packet onto the next page
    public const byte Toc20Ms = 0b11111_0_00;

    public static byte[] Write(int frameCount, int preSkip = 0)
        => Write(Frames(frameCount), preSkip);

    public static byte[] Write(IReadOnlyCollection<AudioFrame> frames, int preSkip = 0)
    {
        var state = new OggOpusWriter.State { SerialNumber = 0x1234_5678 };
        return WriteHeaders(state, preSkip).Concat(WritePage(state, frames, hasNext: false)).ToArray();
    }

    public static byte[] WriteHeaders(OggOpusWriter.State state, int preSkip = 0)
    {
        var buffer = new byte[1024];
        var writer = new OggOpusWriter(state, buffer);
        if (!writer.Write(new OpusHead {
                Version = 1,
                OutputChannelCount = 1,
                PreSkip = (ushort)preSkip,
                InputSampleRate = 48_000,
            }))
            throw new InvalidOperationException("The buffer is too small.");

        var position = writer.Position;
        writer = new OggOpusWriter(state, buffer.AsSpan(position));
        if (!writer.Write(new OpusTags()))
            throw new InvalidOperationException("The buffer is too small.");

        return buffer[..(position + writer.Position)];
    }

    public static byte[] WritePage(OggOpusWriter.State state, IReadOnlyCollection<AudioFrame> frames, bool hasNext)
    {
        var buffer = new byte[64 * 1024];
        var writer = new OggOpusWriter(state, buffer);
        if (!writer.Write(frames, hasNext))
            throw new InvalidOperationException("The buffer is too small.");

        return buffer[..writer.Position];
    }

    public static List<AudioFrame> Frames(int frameCount, int firstIndex = 0)
        => Enumerable.Range(firstIndex, frameCount)
            .Select(i => new AudioFrame {
                Data = Packet(i),
                Offset = Constants.Audio.OpusFrameDuration * i,
            })
            .ToList();

    public static byte[] Packet(int index)
    {
        var length = index % 2 == 0 ? 300 + index % 100 : 50 + index % 100;
        var packet = new byte[length];
        packet[0] = Toc20Ms;
        for (var i = 1; i < length; i++)
            packet[i] = (byte)(index * 31 + i);

        return packet;
    }
}
