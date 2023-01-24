using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract]
public sealed record UserLanguageSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserLanguageSettings);

    [DataMember] public Language Primary { get; init; } = Languages.Main;
    [DataMember] public Language? Secondary { get; init; }
    [DataMember] public string Origin { get; init; } = "";

    public Language Next(Language language)
        => Primary == language
            ? Secondary ?? Primary
            : Primary;
}
