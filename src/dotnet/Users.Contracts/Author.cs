namespace ActualChat.Users;

public record Author : IAuthorLike
{
    public AuthorId Id { get; init; }
    public long Version { get; init; }
    public string Name { get; init; } = "";
    public string Picture { get; init; } = "";
    public bool IsAnonymous { get; init; }
}
