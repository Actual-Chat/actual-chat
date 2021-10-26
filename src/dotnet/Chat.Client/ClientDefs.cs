﻿using RestEase;

namespace ActualChat.Chat.Client;

/// <summary> Should be the same as <see cref="IAuthorServiceFacade"/>. </summary>
[BasePath("author")]
public interface IAuthorServiceFacadeDef
{
    /// <inheritdoc cref="IAuthorServiceFacade.GetByUserId(Session, UserId, CancellationToken)"/>
    [Get(nameof(GetByUserId))]
    Task<Author> GetByUserId(Session session, UserId userId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IAuthorServiceFacade.Get(AuthorId, CancellationToken)"/>
    [Get(nameof(GetByAuthorId))]
    Task<AuthorInfo> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken);
}

[BasePath("chat")]
public interface IChatServiceFacadeDef
{
    // Commands
    [Post(nameof(CreateChat))]
    Task<Chat> CreateChat([Body] ChatCommands.CreateChat command, CancellationToken cancellationToken);
    [Post(nameof(PostMessage))]
    Task<ChatEntry> PostMessage([Body] ChatCommands.PostMessage command, CancellationToken cancellationToken);

    // Queries
    [Get(nameof(TryGet))]
    Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session, ChatId chatId,
        CancellationToken cancellationToken);
    [Get(nameof(GetEntryCount))]
    Task<long> GetEntryCount(
        Session session, ChatId chatId, Range<long>? idRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntries))]
    Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session, ChatId chatId, Range<long> idRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetPermissions))]
    Task<ChatPermissions> GetPermissions(
        Session session, ChatId chatId,
        CancellationToken cancellationToken);
}
