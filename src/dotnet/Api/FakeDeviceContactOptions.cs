namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record FakeDeviceContactOptions(
    [property: DataMember, MemoryPackOrder(0), Key(0)] int ContactCount = 1_000,
    [property: DataMember, MemoryPackOrder(1), Key(1)] int ContactStartIndex = 1,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int PhoneCount = 10,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int EmailCount = 10,
    [property: DataMember, MemoryPackOrder(4), Key(4)] int Seed = 111) : StoredSettings
{
    public const string KvasKey = nameof(FakeDeviceContactOptions);
}
