using System.Reflection;

namespace ActualChat.Db;

internal class LocalIdQueryClosure
{
    public static readonly FieldInfo ShardKeyFieldInfo = typeof(LocalIdQueryClosure).GetField(nameof(LocalIdQueryClosure.ShardKey))!;

    public string? ShardKey;
}
