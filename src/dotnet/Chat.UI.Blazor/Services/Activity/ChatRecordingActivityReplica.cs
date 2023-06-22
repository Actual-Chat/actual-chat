using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatRecordingActivityReplica : IChatRecordingActivity
{
    private readonly SharedResourcePool<ChatId, ChatRecordingActivity>.Lease _lease;
    private readonly IChatRecordingActivity _source;

    public ChatActivity Owner => _source.Owner;
    public ChatId ChatId => _source.ChatId;

    public ChatRecordingActivityReplica(SharedResourcePool<ChatId, ChatRecordingActivity>.Lease lease)
    {
        _lease = lease;
        _source = lease.Resource;
    }

    public void Dispose()
        => _lease.Dispose();

    public Task<ImmutableList<ChatEntry>> GetActiveChatEntries(CancellationToken cancellationToken)
        => _source.GetActiveChatEntries(cancellationToken);

    public Task<ApiArray<AuthorId>> GetActiveAuthorIds(CancellationToken cancellationToken)
        => _source.GetActiveAuthorIds(cancellationToken);

    public Task<bool> IsAuthorActive(AuthorId authorId, CancellationToken cancellationToken)
        => _source.IsAuthorActive(authorId, cancellationToken);
}
