using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatActivity
{
    private readonly SharedResourcePool<ChatId, ChatRecordingActivity> _activityPool;

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
        _activityPool = new SharedResourcePool<ChatId, ChatRecordingActivity>(NewChatRecordingActivity);
    }

    public async Task<IChatRecordingActivity> GetRecordingActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _activityPool.Rent(chatId, cancellationToken).ConfigureAwait(false); // Ok here
        return new ChatRecordingActivityReplica(lease);
    }

    private Task<ChatRecordingActivity> NewChatRecordingActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var chatRecordingActivity = Services.GetRequiredService<ChatRecordingActivity>();
        chatRecordingActivity.ChatId = chatId;
        chatRecordingActivity.Start();
        return Task.FromResult(chatRecordingActivity);
    }
}
