using System.Buffers;

namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioCodec
{
    // Encodes 16kHz mono float PCM frames into Opus packets
    IAsyncEnumerable<IMemoryOwner<byte>> Encode(IAsyncEnumerable<IMemoryOwner<float>> lpcmFrames, CancellationToken cancellationToken = default);

    // Decodes Opus packets into 16kHz mono float PCM frames
    IAsyncEnumerable<IMemoryOwner<float>> Decode(IAsyncEnumerable<IMemoryOwner<byte>> opusPackets, CancellationToken cancellationToken = default);
}
