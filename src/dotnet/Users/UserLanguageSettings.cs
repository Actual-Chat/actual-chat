namespace ActualChat.Users;

[DataContract]
public sealed record UserLanguageSettings
{
    public const string KvasKey = nameof(UserLanguageSettings);

    [DataMember] public LanguageId Primary { get; init; } = LanguageId.Main;
    [DataMember] public LanguageId? Secondary { get; init; }

    public LanguageId Next(LanguageId language)
        => Primary == language
            ? Secondary ?? Primary
            : Primary;
}
