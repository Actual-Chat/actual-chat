namespace ActualChat.Users;

/// <inheritdoc cref="IAuthorInfo"/>
public record UserAuthor(
        string Name = "",
        string? Picture = null,
        string Nickname = "",
        bool IsAnonymous = false)
    : IAuthorInfo;
