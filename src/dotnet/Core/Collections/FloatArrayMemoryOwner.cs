using System.Buffers;

namespace ActualChat.Collections;

public readonly struct FloatArrayMemoryOwner(float[] buffer) : IMemoryOwner<float>
{
    public Memory<float> Memory { get; } = buffer;
    public void Dispose() { }
}
