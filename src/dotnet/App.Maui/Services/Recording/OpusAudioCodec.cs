using System.Buffers;
using ActualChat.UI.Blazor.App.Components;

#if WINDOWS || ANDROID
using OpusSharp.Core.Extensions;
#endif

namespace ActualChat.App.Maui.Services.Recording;

public sealed class OpusAudioCodec : IAudioCodec
{
    public IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        CancellationToken cancellationToken = default)
    {
#if IOS
        return ThrowingAsyncEnumerable<byte>("Audio encoding is not implemented on iOS.");
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

                    await foreach (var frame in lpcmFrames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                        using var _ = frame;
                        var pcm = frame.Memory.Span;
                        if (pcm.Length != Constants.Audio.OpusFrameLength)
                            throw StandardError.Internal($"Invalid frame length {pcm.Length}");

                        var rented = MemoryPool<byte>.Shared.Rent(maxOpusPacketSize);
                        int encodedSize;
                        try {
                            var outSpan = rented.Memory.Span;
                            encodedSize = encoder.Encode(pcm, Constants.Audio.OpusFrameLength, outSpan, maxOpusPacketSize);
                        }
                        catch {
                            rented.Dispose();
                            throw;
                        }

                        if (encodedSize > 0)
                            await channel.Writer
                                .WriteAsync(new PooledSliceOwner(rented, encodedSize), cancellationToken)
                                .ConfigureAwait(false);
                        else
                            rented.Dispose();
                    }
                }
                catch (OperationCanceledException) {
                    /* ignore */
                }
                finally {
                    try { encoder?.Dispose(); }
                    catch {
                        /* ignore */
                    }
                    channel.Writer.TryComplete();
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
        IAsyncEnumerable<IMemoryOwner<byte>> opusPackets,
        CancellationToken cancellationToken = default)
    {
#if IOS
        return ThrowingAsyncEnumerable<float>("Audio decoding is not implemented on iOS.");
#else
        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        _ = Task.Run(async () => {
#if WINDOWS || ANDROID
                OpusSharp.Core.OpusDecoder? decoder = null;
                try {
                    decoder = new OpusSharp.Core.OpusDecoder(
                        Constants.Audio.PlaybackSampleRate,
                        Constants.Audio.Channels);

                    await foreach (var owner in opusPackets.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                        using var _ = owner;
                        var packetMem = owner.Memory;

                        var pcmOwner = MemoryPool<float>.Shared.Rent(Constants.Audio.PcmFrameLength);
                        int samples;
                        try {
                            var packetSpan = packetMem.Span;
                            var pcmSpan = pcmOwner.Memory.Span;
                            samples = decoder.Decode(packetSpan,
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
                    /* ignore */
                }
                finally {
                    try { decoder?.Dispose(); }
                    catch {
                        /* ignore */
                    }
                    channel.Writer.TryComplete();
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

    private static async IAsyncEnumerable<IMemoryOwner<T>> ThrowingAsyncEnumerable<T>(string message)
    {
        await Task.Yield();
        throw new NotImplementedException(message);
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
