using ActualChat.Queues;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record TestEvent1(
    [property: MemoryPackOrder(1)] string? Error) : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Unit ShardKey => default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record TestEvent2 : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Unit ShardKey => default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AddTestEvent1Command(
    [property: MemoryPackOrder(1)] string? Error
) : ICommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AddBothTestEventsCommand : ICommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AddBothTestEventsCommandWithShardKey : ICommand<Unit>, IHasShardKey<ShardKey>, IHasQueueRef
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ShardKey ShardKey { get; init; }

    QueueRef IHasQueueRef.QueueRef => ShardScheme.SlowQueue;
}
