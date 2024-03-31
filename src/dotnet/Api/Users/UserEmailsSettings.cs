using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserEmailsSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserEmailsSettings);

    [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public string TimeZone { get; init; } = "";
}
