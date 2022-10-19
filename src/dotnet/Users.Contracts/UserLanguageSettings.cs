namespace ActualChat.Users;

public sealed record UserLanguageSettings
{
    public const string KvasKey = nameof(UserLanguageSettings);

    public LanguageId Primary { get; init; } = LanguageId.Default;
    public LanguageId? Secondary { get; init; }

    public LanguageId Next(LanguageId language)
        => Primary != language ? Primary : Secondary ?? Primary;
}
