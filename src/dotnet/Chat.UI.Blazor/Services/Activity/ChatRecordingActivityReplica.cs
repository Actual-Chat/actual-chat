using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatRecordingActivityReplica(SharedResourcePool<ChatId, ChatRecordingActivity>.Lease lease)
    : IChatRecordingActivity
{
    private readonly IChatRecordingActivity _source = lease.Resource;

    public ChatActivity Owner => _source.Owner;
    public ChatId ChatId => _source.ChatId;

    public void Dispose()
        => lease.Dispose();

    public Task<ImmutableList<ChatEntry>> GetActiveChatEntries(CancellationToken cancellationToken)
        => _source.GetActiveChatEntries(cancellationToken);

    public Task<ApiArray<AuthorId>> GetActiveAuthorIds(CancellationToken cancellationToken)
        => _source.GetActiveAuthorIds(cancellationToken);

    public Task<bool> IsAuthorActive(AuthorId authorId, CancellationToken cancellationToken)
        => _source.IsAuthorActive(authorId, cancellationToken);
}
