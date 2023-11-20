using Microsoft.IO;

namespace ActualChat.IO;

public static class MemoryStreamManager
{
    public static readonly RecyclableMemoryStreamManager Default = new();
}
