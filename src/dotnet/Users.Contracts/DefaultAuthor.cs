namespace ActualChat.Users;

/// <inheritdoc cref="IAuthorInfo"/>
public record DefaultAuthor(string Name = "", string? Picture = null, string Nickname = "", bool IsAnonymous = false)
    : IAuthorInfo;
