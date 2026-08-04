namespace ActualChat.Core.Server.IntegrationTests.Redis;

[DataContract, MessagePackObject(true)]
public sealed partial record RedisTestValue(
    [property: DataMember] string Name,
    [property: DataMember] int Number
);
