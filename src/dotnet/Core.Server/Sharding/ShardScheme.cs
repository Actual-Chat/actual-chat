namespace ActualChat;

#pragma warning disable CA1000

public class ShardScheme(Symbol id, int shardCount) : IHasId<Symbol>
{
    public static readonly ShardScheme None = new(Symbol.Empty, 0);
    public static readonly ShardScheme Default = new(nameof(Default), 0);
    public static readonly ShardScheme AnyServer = new(nameof(AnyServer), 10); // Mostly for testing
    public static readonly ShardScheme MediaBackend = new(nameof(MediaBackend), 10);
    public static readonly ShardScheme DefaultQueue = new(nameof(DefaultQueue), 1);

    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Id, None },
        // { Default.Id, Default }, // Shouldn't be listed here
        { AnyServer.Id, AnyServer },
        { MediaBackend.Id, MediaBackend },
        // Queues
        { DefaultQueue.Id, DefaultQueue },
    };

    public Symbol Id { get; } = id;
    public int ShardCount { get; } = shardCount;
    public bool IsNone => ReferenceEquals(this, None);
    public bool IsDefault => ReferenceEquals(this, Default);
    public bool IsQueue => Id.Value.OrdinalEndsWith("Queue");

    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);

    public override string ToString()
        => $"{nameof(ShardScheme)}({Id}, {ShardCount})";

    public int GetShardIndex(int shardKey)
        => ShardCount <= 0 ? -1 : shardKey.Mod(ShardCount);

    public ShardScheme NonDefaultOr(ShardScheme shardScheme)
        => IsDefault ? shardScheme : this;
}
