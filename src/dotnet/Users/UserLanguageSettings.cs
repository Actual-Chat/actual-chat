using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserLanguageSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserLanguageSettings);

    [DataMember, MemoryPackOrder(0)] public Language Primary { get; init; } = Languages.Main;
    [DataMember, MemoryPackOrder(1)] public Language? Secondary { get; init; }
    [DataMember, MemoryPackOrder(2)] public string Origin { get; init; } = "";

    public Language Next(Language language)
        => Primary == language
            ? Secondary ?? Primary
            : Primary;
}
