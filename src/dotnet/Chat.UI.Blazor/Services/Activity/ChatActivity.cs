using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatActivity
{
    private readonly SharedResourcePool<Symbol, ChatRecordingActivity> _activityPool;

    internal IServiceProvider Services { get; }
    internal ILogger Log { get; }

    internal Session Session { get; }
    internal IChats Chats { get; }
    internal IStateFactory StateFactory { get; }
    internal MomentClockSet Clocks { get; }

    public ChatActivity(Session session, IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Session = session;
        Chats = services.GetRequiredService<IChats>();
        StateFactory = services.StateFactory();
        Clocks = services.Clocks();
        _activityPool = new SharedResourcePool<Symbol, ChatRecordingActivity>(NewChatRecordingActivity);
    }

    public async Task<IChatRecordingActivity> GetRecordingActivity(Symbol chatId, CancellationToken cancellationToken)
    {
        var lease = await _activityPool.Rent(chatId, cancellationToken).ConfigureAwait(false); // Ok here
        return new ChatRecordingActivityReplica(lease);
    }

    private Task<ChatRecordingActivity> NewChatRecordingActivity(Symbol chatId, CancellationToken cancellationToken)
    {
        var chatRecordingActivity = Services.GetRequiredService<ChatRecordingActivity>();
        chatRecordingActivity.ChatId = chatId;
        _ = chatRecordingActivity.Run();
        return Task.FromResult(chatRecordingActivity);
    }
}
