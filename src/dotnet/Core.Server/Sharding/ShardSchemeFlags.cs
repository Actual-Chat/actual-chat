namespace ActualChat;

#pragma warning disable MA0062, CA2217

[Flags]
public enum ShardSchemeFlags
{
    Queue = 0x1,
    SlowQueue = 0x2 + Queue,
    Backend = 0x10 + Queue,
    Special = 0x100,
}
