using ActualChat.Users;

namespace ActualChat.Chat;

/// <inheritdoc cref="IAuthorInfo"/>
public record class Author : AuthorInfo
{
    public AuthorId AuthorId { get; init; }
    public UserId UserId { get; init; }
    public Author() { }
    public Author(IAuthorInfo info) : base(info) { }
}
