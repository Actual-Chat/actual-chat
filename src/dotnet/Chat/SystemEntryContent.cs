namespace ActualChat.Chat;

public record SystemEntryContent
{
    public MembersChanged? MembersChanged { get; init; }
}

public record MembersChanged
{
    public AuthorId AuthorId { get; init; }
    public bool HasLeft { get; init; }
}
