using ActualChat.Attributes;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record TestEvent1(
    [property: MemoryPackOrder(1)] string? Error) : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record TestEvent2 : EventCommand, IHasShardKey<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Unit ShardKey => Unit.Default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AddTestEvent1Command(
    [property: MemoryPackOrder(1)] string? Error
) : ICommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AddBothTestEventsCommand : ICommand<Unit>;

[Queue(nameof(ShardScheme.TestBackend))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AddBothTestEventsCommandWithShardKey : ICommand<Unit>, IHasShardKey<int>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public int ShardKey { get; init; }
}
