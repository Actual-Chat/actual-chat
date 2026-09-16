namespace ActualChat.Audio;

/// <summary>
/// Sums a dub onto an original, 20 ms frame by frame, ducking the original while the dub speaks:
/// the gain ramps between 1 and the duck gain over a fixed number of samples, and the duck outlives
/// the last dub audio by a hold, so gaps between TTS chunks don't pump the original up and down.
/// </summary>
public sealed class VoiceOverMixer
{
    public const int FrameLength = Constants.Audio.PcmFrameLength;

    private readonly float _duckGain;
    private readonly int _holdFrameCount;
    private readonly float _gainStep;
    private readonly PcmBuffer _dub = new();
    private readonly short[] _dubFrame = new short[FrameLength];
    private float _gain = 1f;
    private int _framesSinceDubAudio;

    public bool HasDubAudio => _dub.Length > 0;
    public bool IsDubSpeaking { get; private set; }

    public VoiceOverMixer(float duckGain, int holdFrameCount, int rampSampleCount)
    {
        _duckGain = duckGain;
        _holdFrameCount = holdFrameCount;
        _gainStep = (1f - duckGain) / Math.Max(1, rampSampleCount);
        _framesSinceDubAudio = holdFrameCount + 1;
    }

    public void AddDubPcm(ReadOnlySpan<byte> pcm)
        => _dub.Append(pcm);

    public void Mix(ReadOnlySpan<short> original, Span<short> output, bool isDubSpeakingElsewhere)
    {
        var hasDubFrame = _dub.TryTake(_dubFrame);
        _framesSinceDubAudio = hasDubFrame ? 0 : _framesSinceDubAudio + 1;
        IsDubSpeaking = isDubSpeakingElsewhere || _framesSinceDubAudio <= _holdFrameCount;
        var targetGain = IsDubSpeaking ? _duckGain : 1f;
        for (var i = 0; i < FrameLength; i++) {
            if (_gain > targetGain)
                _gain = Math.Max(targetGain, _gain - _gainStep);
            else if (_gain < targetGain)
                _gain = Math.Min(targetGain, _gain + _gainStep);
            var originalSample = original.IsEmpty ? 0f : original[i] * _gain;
            var dubSample = hasDubFrame ? _dubFrame[i] : 0;
            output[i] = (short)Math.Clamp(originalSample + dubSample, short.MinValue, short.MaxValue);
        }
    }

    // Nested types

    private sealed class PcmBuffer
    {
        private const int FrameByteLength = FrameLength * sizeof(short);
        private byte[] _bytes = new byte[FrameByteLength * 16];
        private int _start;
        private int _end;

        public int Length => _end - _start;

        public void Append(ReadOnlySpan<byte> chunk)
        {
            if (_end + chunk.Length > _bytes.Length) {
                var length = Length;
                if (length + chunk.Length > _bytes.Length)
                    Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, length + chunk.Length));
                Buffer.BlockCopy(_bytes, _start, _bytes, 0, length);
                _start = 0;
                _end = length;
            }
            chunk.CopyTo(_bytes.AsSpan(_end));
            _end += chunk.Length;
        }

        public bool TryTake(short[] frame)
        {
            // A short tail is padded with silence: the dub's last chunk rarely fills a frame
            var pairedLength = Math.Min(Length, FrameByteLength);
            pairedLength -= pairedLength % 2;
            if (pairedLength == 0)
                return false;

            var sampleCount = pairedLength / sizeof(short);
            MemoryMarshal.Cast<byte, short>(_bytes.AsSpan(_start, pairedLength)).CopyTo(frame);
            Array.Clear(frame, sampleCount, frame.Length - sampleCount);
            _start += pairedLength;
            if (_start == _end)
                _start = _end = 0;
            return true;
        }
    }
}
