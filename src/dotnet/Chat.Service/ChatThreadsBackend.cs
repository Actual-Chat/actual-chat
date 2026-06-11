namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for managing chat thread metadata.
/// </summary>
public class ChatThreadsBackend(IServiceProvider services) : IChatThreadsBackend
{
    private IChatsBackend ChatsBackend => field ??= services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= services.GetRequiredService<IAuthorsBackend>();

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetThreadCreator(ChatId chatId, CancellationToken cancellationToken)
    {
        if (!chatId.IsThread(out var threadChatId))
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var startEntryId = ChatEntryId.New(threadChatId.ParentChatId, threadChatId.ThreadId);
        var startEntry = await ChatsBackend.GetEntry(startEntryId, cancellationToken).ConfigureAwait(false);
        if (startEntry is null)
            return null;

        var creatorId = startEntry.AuthorId;
        return await AuthorsBackend.Get(creatorId.ChatId, creatorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
    }
}
