using ActualChat.Queues;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[DataContract, MessagePackObject(true)]
public partial record TestEvent1(
     string? Error) : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, IgnoreMember]
    public Unit ShardKey => default;
}

[DataContract, MessagePackObject(true)]
public partial record TestEvent2 : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, IgnoreMember]
    public Unit ShardKey => default;
}

[DataContract, MessagePackObject(true)]
public partial record AddTestEvent1Command(
     string? Error
) : ICommand<Unit>;

[DataContract, MessagePackObject(true)]
public partial record AddBothTestEventsCommand : ICommand<Unit>;

[DataContract, MessagePackObject(true)]
public partial record AddBothTestEventsCommandWithShardKey : ICommand<Unit>, IHasShardKey<ShardKey>, IHasQueueRef
{
    [IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey { get; init; }

    QueueRef IHasQueueRef.QueueRef => ShardScheme.SlowQueue;
}
