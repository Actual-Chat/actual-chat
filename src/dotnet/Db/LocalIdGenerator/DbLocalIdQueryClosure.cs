using System.Reflection;

namespace ActualChat.Db;

internal class DbLocalIdQueryClosure
{
    public static readonly FieldInfo ShardKeyFieldInfo =
        typeof(DbLocalIdQueryClosure).GetField(nameof(ShardKey))!;

    public string? ShardKey;
}
