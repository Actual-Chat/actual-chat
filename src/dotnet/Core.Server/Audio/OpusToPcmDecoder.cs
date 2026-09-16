using OpusSharp.Core;

namespace ActualChat.Audio;

// The pipeline carries Opus end to end, so PCM is only ever needed at its edges: ElevenLabs
// realtime accepts PCM and mu-law only, a voice-cloning sample is a WAV, and the voice-over mix
// sums a recording onto 48 kHz TTS. By default this decodes the 20ms frames a recording already
// carries at the rate they were captured, so nothing is resampled; any other Opus rate makes the
// decoder resample to it.

public sealed class OpusToPcmDecoder : IDisposable
{
    private const int Channels = Constants.Audio.Channels;

    private readonly OpusDecoder _decoder;
    private readonly short[] _samples;
    // Opus packets can carry up to 120ms, well above the 20ms our recordings use.
    private readonly int _maxFrameLength;

    public OpusToPcmDecoder(int sampleRate = Constants.Audio.RecordingSampleRate)
    {
        _maxFrameLength = sampleRate / 1000 * 120;
        _decoder = new OpusDecoder(sampleRate, Channels);
        _samples = new short[_maxFrameLength * Channels];
    }

    public void Dispose()
        => _decoder.Dispose();

    public byte[] Decode(ReadOnlySpan<byte> opusFrame)
    {
        if (opusFrame.IsEmpty)
            return [];

        var sampleCount = _decoder.Decode(opusFrame.ToArray(), opusFrame.Length, _samples, _maxFrameLength, false);
        if (sampleCount <= 0)
            return [];

        var pcm = new byte[sampleCount * Channels * sizeof(short)];
        Buffer.BlockCopy(_samples, 0, pcm, 0, pcm.Length);
        return pcm;
    }
}
