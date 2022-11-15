namespace ActualChat.Chat;

public record ServiceEntryDetails
{
    public MembersChangeEntryDetails? MembersChange { get; init; }
}

public record MembersChangeEntryDetails
{
    public Symbol AuthorId { get; init; } = "";
    public bool HasLeft { get; init; }
}
