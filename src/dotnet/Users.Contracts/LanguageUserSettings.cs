namespace ActualChat.Users;

public sealed record LanguageUserSettings
{
    public const string KvasKey = nameof(LanguageUserSettings);
    public LanguageId Primary { get; init; }
    public LanguageId? Secondary { get; init; }

    public LanguageId Next(LanguageId language)
        => Primary != language ? Primary : Secondary ?? Primary;
}
