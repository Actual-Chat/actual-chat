using System.Buffers;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class NSDataMemoryManager(NSData data) : MemoryManager<byte>
{
    protected override void Dispose(bool disposing)
        => data.DisposeSilently();

    public override Span<byte> GetSpan()
    {
        unsafe
        {
            return new Span<byte>((void*)data.Bytes, (int)data.Length);
        }
    }

    public override MemoryHandle Pin(int elementIndex = 0)
        => throw new NotSupportedException();

    public override void Unpin()
    {
    }
}
