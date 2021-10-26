namespace ActualChat.Users;

/// <inheritdoc cref="IAuthorInfo"/>
public record DefaultAuthor(string Name = "", string Picture = "", string Nickname = "", bool IsAnonymous = false)
    : IAuthorInfo;
