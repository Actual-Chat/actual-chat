using System.Buffers;
using Foundation;

namespace ActualChat.App.Maui.Audio;

internal readonly struct NSDataMemoryOwner(NSData data) : IMemoryOwner<byte>
{
    private readonly NSDataMemoryManager _memoryManager = new (data);

    public Memory<byte> Memory => _memoryManager.Memory;

    public void Dispose()
        => _memoryManager.DisposeSilently();
}
