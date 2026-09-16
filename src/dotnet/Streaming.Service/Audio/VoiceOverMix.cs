using ActualChat.Audio;
using OpusSharp.Core;

namespace ActualChat.Streaming;

/// <summary>
/// The S~lang stream: the original with the dub summed on top. The original's frames clock the mix
/// while it runs (one mixed frame per original frame, same offset); after it ends a 20 ms tick
/// drains the dub, whose PCM the synthesizer writes into <see cref="DubPcm"/>, until it completes.
/// </summary>
public sealed class VoiceOverMix(
    AsyncMemoizer<AudioFrame>? original,
    DubActivity activity,
    MomentClockSet clocks,
    ILogger log)
{
    private const int MaxPacketLength = 4096;
    private static readonly TimeSpan FrameDuration = Constants.Audio.OpusFrameDuration;
    private static readonly int HoldFrameCount =
        (int)(Constants.Audio.VoiceOverDuckHold.Ticks / FrameDuration.Ticks);
    private static readonly int RampSampleCount =
        (int)(Constants.Audio.VoiceOverDuckRamp.TotalSeconds * Constants.Audio.PlaybackSampleRate);

    private readonly Channel<byte[]> _dubPcm = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = true,
    });
    private readonly VoiceOverMixer _mixer = new(Constants.Audio.VoiceOverDuckGain, HoldFrameCount, RampSampleCount);
    private readonly short[] _originalPcm = new short[VoiceOverMixer.FrameLength];
    private readonly short[] _mixedPcm = new short[VoiceOverMixer.FrameLength];
    private readonly byte[] _packet = new byte[MaxPacketLength];
    private bool _isDucked;
    private bool _isMixed;
    private bool _isDecodeFailureLogged;
    private bool _isDubFailureLogged;

    private AsyncMemoizer<AudioFrame>? Original { get; } = original;
    private DubActivity Activity { get; } = activity;
    private MomentClock Clock { get; } = clocks.CpuClock;
    private ILogger Log { get; } = log;

    public ChannelWriter<byte[]> DubPcm => _dubPcm.Writer;

    public event Action? Ducked;
    public event Action? Mixed;

    public async Task Run(ChannelWriter<AudioFrame> output, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            using var decoder = new OpusToPcmDecoder(Constants.Audio.PlaybackSampleRate);
            using var encoder = OpusFramePump.NewEncoder();
            // The tail continues the original's offsets; with no original it starts at zero
            var lastOffset = -FrameDuration;
            if (Original != null)
                await foreach (var frame in Original.Replay(cancellationToken).ConfigureAwait(false)) {
                    if (frame.Offset < TimeSpan.Zero)
                        continue; // The stream header

                    DecodeInto(decoder, frame);
                    lastOffset = frame.Offset;
                    await Emit(encoder, output, hasOriginal: true, frame.Offset, cancellationToken)
                        .ConfigureAwait(false);
                }

            // The original is over: the dub tail is paced by the clock from here on
            var tickStartedAt = Clock.Now;
            var tickIndex = 0;
            while (await WaitForDub(cancellationToken).ConfigureAwait(false)) {
                // The wait may have parked for a while: the frames it held back are rebased onto
                // now rather than emitted as a burst to catch up with the old schedule
                var now = Clock.Now;
                if (now > tickStartedAt + FrameDuration * (tickIndex + 1))
                    tickStartedAt = now - FrameDuration * tickIndex;

                tickIndex++;
                var offset = lastOffset + FrameDuration * tickIndex;
                await Emit(encoder, output, hasOriginal: false, offset, cancellationToken).ConfigureAwait(false);
                var delay = tickStartedAt + FrameDuration * tickIndex - Clock.Now;
                if (delay > TimeSpan.Zero)
                    await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
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

    private void DecodeInto(OpusToPcmDecoder decoder, AudioFrame frame)
    {
        var decoded = decoder.Decode(frame.Data.Span);
        if (decoded.Length == 0 && frame.Data.Length != 0 && !_isDecodeFailureLogged) {
            _isDecodeFailureLogged = true;
            Log.LogWarning("Opus frame at {Offset} decoded to nothing, mixing silence for it", frame.Offset);
        }
        var samples = MemoryMarshal.Cast<byte, short>(decoded.AsSpan());
        var count = Math.Min(samples.Length, _originalPcm.Length);
        samples[..count].CopyTo(_originalPcm);
        Array.Clear(_originalPcm, count, _originalPcm.Length - count);
    }

    private ValueTask Emit(
        OpusEncoder encoder,
        ChannelWriter<AudioFrame> output,
        bool hasOriginal,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        DrainDubPcm();
        var now = Clock.Now;
        _mixer.Mix(hasOriginal ? _originalPcm : ReadOnlySpan<short>.Empty, _mixedPcm, Activity.IsSpeaking(now));
        if (_mixer.IsDubSpeaking) {
            Activity.MarkSpeaking(now + Constants.Audio.VoiceOverDuckHold);
            if (!_isDucked) {
                _isDucked = true;
                Ducked?.Invoke();
            }
        }
        if (!_isMixed) {
            _isMixed = true;
            Mixed?.Invoke();
        }
        var length = encoder.Encode(_mixedPcm, VoiceOverMixer.FrameLength, _packet, MaxPacketLength);
        if (length <= 0)
            throw StandardError.Internal($"Opus encoder returned {length}.");

        var frame = new AudioFrame {
            Data = _packet.AsSpan(0, length).ToArray(),
            Offset = offset,
        };
        return output.WriteAsync(frame, cancellationToken);
    }

    private async ValueTask<bool> WaitForDub(CancellationToken cancellationToken)
    {
        // True while there is dub audio to emit; blocks for more until the channel is completed
        while (true) {
            DrainDubPcm();
            if (_mixer.HasDubAudio)
                return true;

            var completion = _dubPcm.Reader.Completion;
            if (completion.IsCompleted) {
                if (completion.Exception != null)
                    OnDubFailed(completion.Exception.GetBaseException());
                return false;
            }

            try {
                if (!await _dubPcm.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    return false;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                // The writer completed the channel with an error while the wait was parked in it
                OnDubFailed(e);
            }
        }
    }

    private void OnDubFailed(Exception error)
    {
        // The parked wait throws it, then the loop sees the faulted completion: one log line
        if (_isDubFailureLogged)
            return;

        _isDubFailureLogged = true;
        Log.LogWarning(error, "Dub audio ended with an error, the mix goes on with what it has");
    }

    private void DrainDubPcm()
    {
        while (_dubPcm.Reader.TryRead(out var chunk))
            _mixer.AddDubPcm(chunk);
    }
}
