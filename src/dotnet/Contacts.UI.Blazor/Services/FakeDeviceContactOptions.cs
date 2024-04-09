using MemoryPack;

namespace ActualChat.Contacts.UI.Blazor.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record FakeDeviceContactOptions(
    [property: DataMember, MemoryPackOrder(0)] int ContactCount = 1_000,
    [property: DataMember, MemoryPackOrder(1)] int ContactStartIndex = 1,
    [property: DataMember, MemoryPackOrder(2)] int PhoneCount = 10,
    [property: DataMember, MemoryPackOrder(3)] int EmailCount = 10,
    [property: DataMember, MemoryPackOrder(4)] int Seed = 111)
{
    public const string KvasKey = nameof(FakeDeviceContactOptions);
}
