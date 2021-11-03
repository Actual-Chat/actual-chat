using RestEase;

namespace ActualChat.Chat.Frontend.Client;

/// <summary> Should be the same as <see cref="IAuthorServiceFrontend"/>. </summary>
[BasePath("author")]
public interface IAuthorServiceFrontendDef
{
    /// <inheritdoc cref="IAuthorServiceFacade.GetByUserId(Session, UserId, CancellationToken)"/>
    [Get(nameof(GetByUserIdAndChatId))]
    Task<Author?> GetByUserIdAndChatId(Session session, UserId userId, ChatId chatId, CancellationToken cancellationToken);

    /// <inheritdoc cref="IAuthorServiceFacade.Get(AuthorId, CancellationToken)"/>
    [Get(nameof(GetByAuthorId))]
    Task<AuthorInfo> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken);
}
