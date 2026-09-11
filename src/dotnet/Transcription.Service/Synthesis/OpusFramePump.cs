using ActualChat.Audio;
using OpusSharp.Core;
using OpusSharp.Core.Extensions;

namespace ActualChat.Transcription;

/// <summary>
/// Encodes 48 kHz mono PCM into 20 ms Opus <see cref="AudioFrame"/>s emitted at wall-clock pace;
/// every gap in the input becomes encoded silence, so offsets stay contiguous from zero.
/// </summary>
public sealed class OpusFramePump : IDisposable
{
    public const int SampleRate = 48_000;
    public const int FrameLength = SampleRate / 1000 * Constants.Audio.OpusFrameDurationMs;
    public const int FrameByteLength = FrameLength * sizeof(short);
    private const int MaxPacketLength = 4096;

    private readonly OpusEncoder _encoder;
    private readonly short[] _pcm = new short[FrameLength];
    private readonly byte[] _packet = new byte[MaxPacketLength];
    private readonly PcmBuffer _buffer = new();

    private MomentClock Clock { get; }

    public OpusFramePump(MomentClock clock)
    {
        Clock = clock;
        _encoder = new OpusEncoder(SampleRate, Constants.Audio.Channels, OpusPredefinedValues.OPUS_APPLICATION_VOIP);
        _encoder.SetBitRate(Constants.Audio.Bitrate);
        _encoder.SetVbr(true);
        _encoder.SetSignal(OpusPredefinedValues.OPUS_SIGNAL_VOICE);
    }

    public void Dispose()
        => _encoder.Dispose();

    public async Task Run(
        ChannelReader<byte[]> pcm,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var startedAt = Clock.Now;
            var frameIndex = 0;
            while (true) {
                // Completion is read before the drain, so a chunk written right before Complete()
                // is always drained by the pass that observes the completion.
                var isInputCompleted = pcm.Completion.IsCompleted;
                while (pcm.TryRead(out var chunk))
                    _buffer.Append(chunk);
                if (isInputCompleted && _buffer.Length == 0)
                    break;

                if (!_buffer.TryTake(_pcm, mustPadTail: isInputCompleted))
                    Array.Clear(_pcm);
                var frame = Encode(frameIndex++);
                var delay = startedAt + Constants.Audio.OpusFrameDuration * frameIndex - Clock.Now;
                if (delay > TimeSpan.Zero)
                    await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            await pcm.Completion.ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            output.TryComplete(error);
        }
    }

    // Private methods

    private AudioFrame Encode(int frameIndex)
    {
        var length = _encoder.Encode(_pcm, FrameLength, _packet, MaxPacketLength);
        if (length <= 0)
            throw StandardError.Internal($"Opus encoder returned {length}.");

        return new AudioFrame {
            Data = _packet.AsSpan(0, length).ToArray(),
            Offset = Constants.Audio.OpusFrameDuration * frameIndex,
        };
    }

    // Nested types

    private sealed class PcmBuffer
    {
        private byte[] _bytes = new byte[FrameByteLength * 16];
        private int _start;
        private int _end;

        public int Length => _end - _start;

        public void Append(byte[] chunk)
        {
            if (_end + chunk.Length > _bytes.Length) {
                var length = Length;
                if (length + chunk.Length > _bytes.Length)
                    Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, length + chunk.Length));
                Buffer.BlockCopy(_bytes, _start, _bytes, 0, length);
                _start = 0;
                _end = length;
            }
            Buffer.BlockCopy(chunk, 0, _bytes, _end, chunk.Length);
            _end += chunk.Length;
        }

        public bool TryTake(short[] frame, bool mustPadTail)
        {
            var length = Length;
            if (length < FrameByteLength && !(mustPadTail && length > 0))
                return false;

            var takeLength = Math.Min(length, FrameByteLength);
            var takeSampleCount = takeLength / sizeof(short);
            MemoryMarshal.Cast<byte, short>(_bytes.AsSpan(_start, takeLength)).CopyTo(frame);
            Array.Clear(frame, takeSampleCount, frame.Length - takeSampleCount);
            _start += takeLength;
            if (_start == _end)
                _start = _end = 0;
            return true;
        }
    }
}
