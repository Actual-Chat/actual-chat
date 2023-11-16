using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatActivity
{
    private readonly SharedResourcePool<ChatId, ChatStreamingActivity> _activityPool;

    internal IServiceProvider Services { get; }
    internal ILogger Log { get; }

    internal Session Session { get; }
    internal IChats Chats { get; }
    internal IStateFactory StateFactory { get; }
    internal MomentClockSet Clocks { get; }

    public ChatActivity(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Session = services.Session();
        Chats = services.GetRequiredService<IChats>();
        StateFactory = services.StateFactory();
        Clocks = services.Clocks();
        _activityPool = new SharedResourcePool<ChatId, ChatStreamingActivity>(NewChatStreamingActivity);
    }

    public async Task<IChatStreamingActivity> GetStreamingActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _activityPool.Rent(chatId, cancellationToken).ConfigureAwait(false); // Ok here
        return new ChatStreamingActivityReplica(lease);
    }

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
