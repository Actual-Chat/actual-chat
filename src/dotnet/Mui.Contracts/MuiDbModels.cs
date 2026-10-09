namespace ActualChat.Mui;

[DataContract, MessagePackObject]
public sealed partial record MuiColumnInfo(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] string Type,
    [property: DataMember, Key(2)] bool IsNullable,
    [property: DataMember, Key(3)] bool IsKey);

[DataContract, MessagePackObject]
public sealed partial record MuiTableInfo(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] MuiColumnInfo[] Columns);

[DataContract, MessagePackObject]
public sealed partial record MuiDatabaseInfo(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] string Kind,
    [property: DataMember, Key(2)] string[] Tenants,
    [property: DataMember, Key(3)] MuiTableInfo[] Tables,
    [property: DataMember, Key(4)] string CreateScript);

[DataContract, MessagePackObject]
public sealed partial record MuiSqlQuery(
    [property: DataMember, Key(0)] string Database,
    [property: DataMember, Key(1)] string Sql,
    [property: DataMember, Key(2)] int MaxRows = 1000,
    [property: DataMember, Key(3)] int TimeoutMs = 30_000,
    [property: DataMember, Key(4)] string? CacheKey = null) : IHasShardKey
{
    // A result never has more rows than this; the backend enforces it
    public const int MaxRowsLimit = 500;

    // The same query goes to the same backend, whichever host it comes from
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(Sql);
}

[DataContract, MessagePackObject]
public sealed partial record MuiSqlColumn(
    [property: DataMember, Key(0)] string Name,
    [property: DataMember, Key(1)] string Type);

[DataContract, MessagePackObject]
public sealed partial record MuiSqlResult(
    [property: DataMember, Key(0)] MuiSqlColumn[] Columns,
    [property: DataMember, Key(1)] string?[][] Rows,
    [property: DataMember, Key(2)] bool IsTruncated,
    [property: DataMember, Key(3)] int ElapsedMs,
    [property: DataMember, Key(4)] string HostId);

public static class MuiTableInfoExt
{
    // Primary key columns first, then "version" if there is one, then the other columns by name
    public static MuiColumnInfo[] GetOrderedColumns(this MuiTableInfo table)
        => table.Columns
            .OrderBy(x => x.IsKey ? 0 : x.Name == "version" ? 1 : 2)
            .ThenBy(x => x.Name)
            .ToArray();
}
