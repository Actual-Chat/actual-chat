using System.Buffers;
using ActualChat.Audio;

namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioCodec
{
    // Encoder delay a player must drop from the start of a stream, in the Opus-defined 48 kHz samples
    // (not the encoder's 16 kHz input rate). See Constants.Audio.OpusPreSkip.
    int EncoderPreSkip { get; }

    // Encodes 16kHz mono float PCM frames into Opus packets
    IAsyncEnumerable<IMemoryOwner<byte>> Encode(
        IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames,
        CancellationToken cancellationToken = default);

    // Decodes Opus packets into 16kHz mono float PCM frames
    IAsyncEnumerable<IMemoryOwner<float>> Decode(
        IAsyncEnumerable<AudioFrame> opusPackets,
        CancellationToken cancellationToken = default);
}
