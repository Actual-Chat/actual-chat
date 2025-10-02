using System.Buffers;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Opus.MaciOS;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public class IosAudioCodec(AppUIHub hub) : IAudioCodec
{
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.Services.LogFor(GetType());

    public async IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
        try
        {
            var buffer = new AVAudioPcmBuffer(AudioEngine.VoiceRecordingFormat, (uint)frame.Memory.Length);
            unsafe {
                var dstSpan = new Span<float>((void*)buffer.AudioBufferList[0].Data, frame.Memory.Length);
                frame.Memory.Span.CopyTo(dstSpan);
            }
            buffer.FrameLength = (uint)frame.Memory.Length;
            return Encode(encoder, buffer);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Failed to encode frame: {Length}B", frame.Memory.Length);
            throw;
        }
    }

    private NSData Encode(OpusEncoder encoder, AVAudioPcmBuffer buffer)
    {
        try
        {
            var data = encoder.Encode(buffer, out var error);
            error.Assert();
            // Log.LogInformation("!!! Encoded buffer {Data}", Convert.ToHexString(data!.ToArray()));
            return data!;
        }
        catch (Exception e)
        {
            Log.LogError(e, "!!! Failed to encode buffer {Buffer}({Format})", buffer, buffer.Format);
            // Log.LogError(e, "!!! Failed to encode buffer {Buffer}({Format}) {Data}", buffer, buffer.Format, Convert.ToHexString(buffer.ToBytes()));
            throw;
        }
    }

    public IAsyncEnumerable<IMemoryOwner<float>> Decode(IAsyncEnumerable<IMemoryOwner<byte>> opusPackets, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
