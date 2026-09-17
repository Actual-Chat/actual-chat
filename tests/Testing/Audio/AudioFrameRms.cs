using ActualChat.Audio;

namespace ActualChat.Testing.Audio;

/// <summary>
/// The RMS level of a run of Opus frames, decoded at the playback rate - what a level assertion
/// on a mix or a recording compares.
/// </summary>
public static class AudioFrameRms
{
    public static double Of(IReadOnlyList<AudioFrame> frames)
        => Of(frames, ..);

    public static double Of(IReadOnlyList<AudioFrame> frames, System.Range range)
    {
        // Every frame is decoded in order - a decoder with no history under-delivers on its first
        // frame - and only the ones in range count
        var (offset, length) = range.GetOffsetAndLength(frames.Count);
        using var decoder = new OpusToPcmDecoder(Constants.Audio.PlaybackSampleRate);
        double sum = 0;
        var count = 0;
        for (var i = 0; i < frames.Count; i++) {
            var samples = MemoryMarshal.Cast<byte, short>(decoder.Decode(frames[i].Data.Span));
            if (i < offset || i >= offset + length)
                continue;

            foreach (var s in samples) {
                sum += (double)s * s;
                count++;
            }
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }
}
