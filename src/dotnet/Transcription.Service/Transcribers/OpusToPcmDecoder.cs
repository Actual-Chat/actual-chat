using OpusSharp.Core;

namespace ActualChat.Transcription;

// Every other transcriber takes Opus, so the pipeline never decodes it server-side. ElevenLabs
// realtime accepts PCM and mu-law only, hence this one - it decodes the 20ms frames a recording
// already carries, at the rate they were captured, so nothing is resampled.

public sealed class OpusToPcmDecoder : IDisposable
{
    private const int SampleRate = Constants.Audio.RecordingSampleRate;
    private const int Channels = Constants.Audio.Channels;
    // Opus packets can carry up to 120ms, well above the 20ms our recordings use.
    private const int MaxFrameLength = SampleRate / 1000 * 120;

    private readonly OpusDecoder _decoder = new(SampleRate, Channels);
    private readonly short[] _samples = new short[MaxFrameLength * Channels];

    public void Dispose()
        => _decoder.Dispose();

    public byte[] Decode(ReadOnlySpan<byte> opusFrame)
    {
        if (opusFrame.IsEmpty)
            return [];

        var sampleCount = _decoder.Decode(opusFrame.ToArray(), opusFrame.Length, _samples, MaxFrameLength, false);
        if (sampleCount <= 0)
            return [];

        var pcm = new byte[sampleCount * Channels * sizeof(short)];
        Buffer.BlockCopy(_samples, 0, pcm, 0, pcm.Length);
        return pcm;
    }
}
