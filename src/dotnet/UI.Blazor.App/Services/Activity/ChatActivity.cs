using ActualChat.Pooling;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatActivity : ScopedServiceBase<ChatUIHub>, IAsyncDisposable
{
    private readonly SharedResourcePool<ChatId, ChatStreamingActivity> _activityPool;

    public ChatActivity(ChatUIHub hub) : base(hub)
        => _activityPool = new SharedResourcePool<ChatId, ChatStreamingActivity>(NewChatStreamingActivity);

    public async Task<IChatStreamingActivity> GetStreamingActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _activityPool.Rent(chatId, cancellationToken).ConfigureAwait(false); // Ok here
        return new ChatStreamingActivityReplica(lease);
    }

    public ValueTask DisposeAsync()
        => _activityPool.DisposeAsync();

    private Task<ChatStreamingActivity> NewChatStreamingActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var chatStreamingActivity = Services.GetRequiredService<ChatStreamingActivity>();
        chatStreamingActivity.ChatId = chatId;
        chatStreamingActivity.Start();
        return Task.FromResult(chatStreamingActivity);
    }
}
