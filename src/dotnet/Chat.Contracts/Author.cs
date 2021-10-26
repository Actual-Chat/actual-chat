using ActualChat.Users;

namespace ActualChat.Chat;

/// <inheritdoc cref="IAuthorInfo"/>
public record class Author : AuthorInfo
{
    public UserId UserId { get; init; } = UserId.None;
    public Author() { }
    public Author(IAuthorInfo info) : base(info) { }
}