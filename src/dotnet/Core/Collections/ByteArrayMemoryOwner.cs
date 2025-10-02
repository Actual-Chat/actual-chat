using System.Buffers;

namespace ActualChat.Collections;

public readonly struct ByteArrayMemoryOwner(byte[] buffer) : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; } = buffer;
    public void Dispose() { }
}
