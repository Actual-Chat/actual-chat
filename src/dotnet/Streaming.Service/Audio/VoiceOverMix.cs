using ActualChat.Audio;

namespace ActualChat.Streaming;

/// <summary>
/// The S~lang stream: the original with the dub summed on top. The original's frames clock the mix
/// while it runs (one mixed frame per original frame, same offset); after it ends a 20 ms tick
/// drains the dub, whose PCM the synthesizer writes into <see cref="DubPcm"/> as it arrives.
/// </summary>
public sealed class VoiceOverMix(
    AsyncMemoizer<AudioFrame>? original,
    DubActivity activity,
    MomentClockSet clocks,
    ILogger log)
{
    private static readonly int HoldFrameCount =
        (int)(Constants.Audio.VoiceOverDuckHold.Ticks / Constants.Audio.OpusFrameDuration.Ticks);
    private static readonly int RampSampleCount =
        (int)(Constants.Audio.VoiceOverDuckRamp.TotalSeconds * Constants.Audio.PlaybackSampleRate);

    private readonly Channel<byte[]> _dubPcm = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = true,
    });
    private readonly VoiceOverMixer _mixer = new(Constants.Audio.VoiceOverDuckGain, HoldFrameCount, RampSampleCount);
    private readonly short[] _originalPcm = new short[VoiceOverMixer.FrameLength];
    private readonly short[] _mixedPcm = new short[VoiceOverMixer.FrameLength];
    // Written by the synthesizer's thread, polled by the tick loop
    private bool _isSynthesisStarted;
    private bool _isDucked;
    private bool _isMixed;
    private bool _isDecodeFailureLogged;

    private AsyncMemoizer<AudioFrame>? Original { get; } = original;
    private DubActivity Activity { get; } = activity;
    private MomentClock Clock { get; } = clocks.CpuClock;
    private ILogger Log { get; } = log;

    public ChannelWriter<byte[]> DubPcm => _dubPcm.Writer;

    public event Action? Ducked;
    public event Action? Mixed;

    public void OnSynthesisStarted()
        // Once called, the mix outlives the original until DubPcm completes
        => Volatile.Write(ref _isSynthesisStarted, true);

    public async Task Run(ChannelWriter<AudioFrame> output, CancellationToken cancellationToken)
    {
        // The pump only encodes here: pacing comes from the original's frames, then from the tick
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var pump = new OpusFramePump(Clock, isPaced: false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await TaskExt.WhenPushAndRead(
                Produce(pcm.Writer, cts.Token),
                pump.Run(pcm.Reader, output, cts.Token),
                cts)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task Produce(ChannelWriter<byte[]> pcm, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            using var decoder = new OpusToPcmDecoder(Constants.Audio.PlaybackSampleRate);
            if (Original != null)
                await foreach (var frame in Original.Replay(cancellationToken).ConfigureAwait(false)) {
                    if (frame.Offset < TimeSpan.Zero)
                        continue; // The stream header

                    DecodeInto(decoder, frame);
                    await Emit(pcm, hasOriginal: true, cancellationToken).ConfigureAwait(false);
                }

            // The original is over: the dub tail is paced by the clock from here on
            var tickStartedAt = Clock.Now;
            var tickIndex = 0;
            while (await WaitForDub(cancellationToken).ConfigureAwait(false)) {
                await Emit(pcm, hasOriginal: false, cancellationToken).ConfigureAwait(false);
                tickIndex++;
                var delay = tickStartedAt + Constants.Audio.OpusFrameDuration * tickIndex - Clock.Now;
                if (delay > TimeSpan.Zero)
                    await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            pcm.TryComplete(error);
        }
    }

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

    private ValueTask Emit(ChannelWriter<byte[]> pcm, bool hasOriginal, CancellationToken cancellationToken)
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
        return pcm.WriteAsync(MemoryMarshal.AsBytes(_mixedPcm.AsSpan()).ToArray(), cancellationToken);
    }

    private void DrainDubPcm()
    {
        while (_dubPcm.Reader.TryRead(out var chunk))
            _mixer.AddDubPcm(chunk);
    }

    private async ValueTask<bool> WaitForDub(CancellationToken cancellationToken)
    {
        // True while there is dub audio to emit or a synthesis that may still deliver some
        while (true) {
            DrainDubPcm();
            if (_mixer.HasDubAudio)
                return true;
            if (!Volatile.Read(ref _isSynthesisStarted) || _dubPcm.Reader.Completion.IsCompleted)
                return false;

            if (!await _dubPcm.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                return false;
        }
    }
}
