using System.Buffers;
using ActualChat.Audio;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleAudioCodec(AppUIHub hub) : IAudioCodec
{
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public async IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using var encoder = Opus.CreateEncoder();
        await foreach (var frame in lpcmFrames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            using var _1 = frame;
            yield return EncodeFrame(frame, encoder);
        }
    }

    private NSDataMemoryOwner EncodeFrame(IMemoryOwner<float> frame, OpusEncoder encoder)
    {
        try {
            var buffer = frame.ToPcmBuffer(AudioEngine.VoiceRecordingFormat);
            var data = encoder.Encode(buffer, out var error);
            error.Assert();
            return new NSDataMemoryOwner(data!);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to encode frame: {Length}B", frame.Memory.Length);
            throw;
        }
    }

    public IAsyncEnumerable<IMemoryOwner<float>> Decode(
        IAsyncEnumerable<AudioFrame> opusPackets, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("AppleAudioCodec is encode-only; decoding goes through OpusDecoder.");
}
