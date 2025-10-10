using System.Buffers;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Opus.MaciOS;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class IosAudioCodec(AppUIHub hub) : IAudioCodec
{
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public async IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using var encoder = Opus.CreateEncoder();
        await foreach (var frame in lpcmFrames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            using var _1 = frame;
            var data = EncodeFrame(frame, encoder);
            yield return new NSDataMemoryOwner(data);
        }
    }

    private NSData EncodeFrame(IMemoryOwner<float> frame, OpusEncoder encoder)
    {
        try {
            var buffer = new AVAudioPcmBuffer(AudioEngine.VoiceRecordingFormat, (uint)frame.Memory.Length);
            unsafe {
                var dstSpan = new Span<float>((void*)buffer.AudioBufferList[0].Data, frame.Memory.Length);
                frame.Memory.Span.CopyTo(dstSpan);
            }
            buffer.FrameLength = (uint)frame.Memory.Length;
            var data = encoder.Encode(buffer, out var error);
            error.Assert();
            return data!;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to encode frame: {Length}B", frame.Memory.Length);
            throw;
        }
    }

    public IAsyncEnumerable<IMemoryOwner<float>> Decode(IAsyncEnumerable<IMemoryOwner<byte>> opusPackets, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
