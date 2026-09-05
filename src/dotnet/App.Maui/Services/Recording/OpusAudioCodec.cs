using System.Buffers;
using ActualChat.Audio;
using ActualChat.UI.Blazor.App.Components;

#if WINDOWS || ANDROID
using OpusSharp.Core.Extensions;
#endif

namespace ActualChat.App.Maui.Services.Recording;

#pragma warning disable CS9113 // 'log' is read only under WINDOWS || ANDROID
public sealed class OpusAudioCodec(ILogger<OpusAudioCodec> log) : IAudioCodec
{
    public IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        CancellationToken cancellationToken = default)
    {
#if IOS
        return NotSupportedAsyncEnumerable<byte>("Audio encoding is not supported on iOS.");
#else
        var channel = Channel.CreateUnbounded<IMemoryOwner<byte>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        _ = Task.Run(async () => {
                const int maxOpusPacketSize = 4096;

#if WINDOWS || ANDROID
                OpusSharp.Core.OpusEncoder? encoder = null;
                Exception? error = null;
                try {
                    encoder = new OpusSharp.Core.OpusEncoder(
                        Constants.Audio.RecordingSampleRate,
                        Constants.Audio.Channels,
                        OpusSharp.Core.OpusPredefinedValues.OPUS_APPLICATION_VOIP);
                    encoder.SetComplexity(10);
                    encoder.SetBitRate(Constants.Audio.Bitrate);
                    encoder.SetVbr(true);
                    encoder.SetVbrConstraint(true);
                    encoder.SetBandwidth(OpusSharp.Core.OpusPredefinedValues.OPUS_AUTO);
                    encoder.SetMaxBandwidth((int)OpusSharp.Core.OpusPredefinedValues.OPUS_BANDWIDTH_FULLBAND); // Probably wrong contract
                    encoder.SetSignal(OpusSharp.Core.OpusPredefinedValues.OPUS_SIGNAL_VOICE);
                    encoder.SetLsbDepth(18);
                    encoder.SetInbandFec(0);
                    encoder.SetPacketLostPercent(3);
                    encoder.SetPredictionDisabled(false);
                    var skipFrames = encoder.GetLookahead();

                    // Cadence tracking: warn when wall-clock between successful encode-outputs
                    // drifts above 60ms (= 3 frames worth of expected 20ms pace), aggregated
                    // per 1s window. Also tracks max per-call encode latency (CPU-bound work)
                    // and GC collections within the window. Together these distinguish:
                    //   - encoder itself slow (encodeMaxCallMs spikes)
                    //   - encoder fast but downstream backpressured (encodeMaxCallMs low,
                    //     but cadence gaps + WriteAsync stalls)
                    //   - GC-induced pauses (gen0/1/2 deltas non-zero in gap windows).
                    var lastEncodeStamp = 0L;
                    var lastLogStamp = Stopwatch.GetTimestamp();
                    var encodeGapsInWindow = 0;
                    var encodeMaxGapMs = 0.0;
                    var encodeMaxCallMs = 0.0;
                    var gen0Start = GC.CollectionCount(0);
                    var gen1Start = GC.CollectionCount(1);
                    var gen2Start = GC.CollectionCount(2);

                    await foreach (var frame in lpcmFrames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                        using var _ = frame;
                        var pcm = frame.Memory.Span;
                        if (pcm.Length != Constants.Audio.OpusFrameLength)
                            throw StandardError.Internal($"Invalid frame length {pcm.Length}");

                        var rented = ArrayPools.SharedBytePool.LeaseArrayOwner(maxOpusPacketSize);
                        int encodedSize;
                        var encodeCallStart = Stopwatch.GetTimestamp();
                        try {
                            var outSpan = rented.Span;
                            encodedSize = encoder.Encode(pcm, Constants.Audio.OpusFrameLength, outSpan, maxOpusPacketSize);
                        }
                        catch {
                            rented.Dispose();
                            throw;
                        }
                        var encodeCallMs = Stopwatch.GetElapsedTime(encodeCallStart).TotalMilliseconds;
                        if (encodeCallMs > encodeMaxCallMs)
                            encodeMaxCallMs = encodeCallMs;

                        if (encodedSize > 0)
                            await channel.Writer
                                .WriteAsync(new PooledSliceOwner(rented, encodedSize), cancellationToken)
                                .ConfigureAwait(false);
                        else
                            rented.Dispose();

                        var nowStamp = Stopwatch.GetTimestamp();
                        if (lastEncodeStamp != 0) {
                            var deltaMs = Stopwatch.GetElapsedTime(lastEncodeStamp, nowStamp).TotalMilliseconds;
                            if (deltaMs > 60.0) {
                                encodeGapsInWindow++;
                                if (deltaMs > encodeMaxGapMs)
                                    encodeMaxGapMs = deltaMs;
                            }
                        }
                        lastEncodeStamp = nowStamp;

                        if (Stopwatch.GetElapsedTime(lastLogStamp, nowStamp).TotalSeconds >= 1.0) {
                            var gen0 = GC.CollectionCount(0) - gen0Start;
                            var gen1 = GC.CollectionCount(1) - gen1Start;
                            var gen2 = GC.CollectionCount(2) - gen2Start;
                            if (encodeGapsInWindow > 0)
                                log.LogWarning(
                                    "opus-encode-cadence: {Gaps} gap(s) >60ms in last second; max gap {MaxMs:F0}ms; max encode-call {MaxCallMs:F1}ms; gc 0/1/2={Gen0}/{Gen1}/{Gen2}",
                                    encodeGapsInWindow, encodeMaxGapMs, encodeMaxCallMs, gen0, gen1, gen2);
                            encodeGapsInWindow = 0;
                            encodeMaxGapMs = 0;
                            encodeMaxCallMs = 0;
                            gen0Start += gen0; gen1Start += gen1; gen2Start += gen2;
                            lastLogStamp = nowStamp;
                        }
                    }
                }
                catch (OperationCanceledException) {
                    /* Ignore */
                }
                catch (Exception e) {
                    // Completing without it would tell the reader the utterance ended normally,
                    // and this task is discarded, so the throw would surface nowhere at all.
                    log.LogError(e, "Opus encoding failed");
                    error = e;
                }
                finally {
                    try { encoder?.Dispose(); }
                    catch {
                        /* Ignore */
                    }
                    channel.Writer.TryComplete(error);
                }
#else
            // Other platforms default to not implemented to avoid accidental use without Opus
            try {
                await foreach (var _ in lpcmFrames.WithCancellation(cancellationToken)) { }
            }
            finally {
                channel.Writer.TryComplete(new NotImplementedException("Audio encoding is not implemented on this platform."));
            }
#endif
            },
            cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
#endif
    }

    public IAsyncEnumerable<IMemoryOwner<float>> Decode(
        IAsyncEnumerable<AudioFrame> opusPackets,
        CancellationToken cancellationToken = default)
    {
#if IOS
        return NotSupportedAsyncEnumerable<float>("Audio decoding is not supported on iOS.");
#else
        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        _ = Task.Run(async () => {
#if WINDOWS || ANDROID
                OpusSharp.Core.OpusDecoder? decoder = null;
                Exception? error = null;
                try {
                    decoder = new OpusSharp.Core.OpusDecoder(
                        Constants.Audio.PlaybackSampleRate,
                        Constants.Audio.Channels);

                    await foreach (var frame in opusPackets.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                        // TODO(AK): Dispose frame when IDisposable will be implemented
                        var packetMem = frame.Data;

                        var pcmOwner = ArrayPools.SharedFloatPool.LeaseArrayOwner(Constants.Audio.PcmFrameLength);
                        int samples;
                        try {
                            var packetSpan = packetMem.Span;
                            var pcmSpan = pcmOwner.Span;
                            samples = decoder.Decode(packetSpan.AsSpanUnsafe(),
                                packetSpan.Length,
                                pcmSpan,
                                Constants.Audio.PcmFrameLength,
                                false);
                        }
                        catch {
                            pcmOwner.Dispose();
                            throw;
                        }

                        if (samples > 0)
                            await channel.Writer
                                .WriteAsync(new PooledSliceOwner<float>(pcmOwner, samples), cancellationToken)
                                .ConfigureAwait(false);
                        else
                            pcmOwner.Dispose();
                    }
                }
                catch (OperationCanceledException) {
                    /* Ignore */
                }
                catch (Exception e) {
                    // Completing without it would tell the reader the track ended normally, and
                    // this task is discarded, so the throw would surface nowhere at all.
                    log.LogError(e, "Opus decoding failed");
                    error = e;
                }
                finally {
                    try { decoder?.Dispose(); }
                    catch {
                        /* Ignore */
                    }
                    channel.Writer.TryComplete(error);
                }
#else
            try {
                await foreach (var _ in opusPackets.WithCancellation(cancellationToken)) { }
            }
            finally {
                channel.Writer.TryComplete(new NotImplementedException("Audio decoding is not implemented on this platform."));
            }
#endif
            },
            cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
#endif
    }

    private static async IAsyncEnumerable<IMemoryOwner<T>> NotSupportedAsyncEnumerable<T>(string message)
    {
        await Task.Yield(); // Just to suppress the warning
        throw new NotSupportedException(message);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private readonly struct PooledSliceOwner<T>(IMemoryOwner<T> rented, int length) : IMemoryOwner<T>
    {
        public Memory<T> Memory => rented.Memory[..length];
        public void Dispose() => rented.Dispose();
    }

    private readonly struct PooledSliceOwner(IMemoryOwner<byte> rented, int length) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => rented.Memory[..length];
        public void Dispose() => rented.Dispose();
    }
}
