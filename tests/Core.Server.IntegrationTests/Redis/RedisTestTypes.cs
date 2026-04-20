namespace ActualChat.Core.Server.IntegrationTests.Redis;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record RedisTestValue(
    [property: DataMember, MemoryPackOrder(0)] string Name,
    [property: DataMember, MemoryPackOrder(1)] int Number
);
