using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatStreamingActivityReplica(SharedResourcePool<ChatId, ChatStreamingActivity>.Lease lease)
    : IChatStreamingActivity
{
    private readonly IChatStreamingActivity _source = lease.Resource;

    public ChatActivity Owner => _source.Owner;
    public ChatId ChatId => _source.ChatId;
    public IState<Moment?> LastTranscribedAt => _source.LastTranscribedAt;

    public void Dispose()
        => lease.Dispose();

    public Task<ImmutableList<ChatEntry>> GetStreamingEntries(CancellationToken cancellationToken)
        => _source.GetStreamingEntries(cancellationToken);

    public Task<ApiArray<AuthorId>> GetStreamingAuthorIds(CancellationToken cancellationToken)
        => _source.GetStreamingAuthorIds(cancellationToken);

    public Task<bool> IsAuthorStreaming(AuthorId authorId, CancellationToken cancellationToken)
        => _source.IsAuthorStreaming(authorId, cancellationToken);
}
