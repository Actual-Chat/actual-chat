using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class LocationActivitySource(AppUIHub hub) : IActivitySource, IHasDisposeStatus
{
    private LiveLocationReporter LiveLocationReporter
        => field ??= hub.Services.GetRequiredService<LiveLocationReporter>();
    public bool IsDisposed => false;

    [ComputeMethod]
    public virtual async Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken)
    {
        var chatIds = await LiveLocationReporter.GetActiveShareChatIds(cancellationToken).ConfigureAwait(false);
        if (chatIds.IsEmpty)
            return null;

        var chatId = chatIds[0];
        var chat = await hub.Chats.Get(hub.Session, chatId, cancellationToken).ConfigureAwait(false);
        // The picture stays empty: the location notification names its chat but shows no avatar.
        var chatInfo = new ActivityChatInfo(chatId, chat?.Title ?? "", "", chatIds.Length - 1);
        return new LocationActivity(chatInfo);
    }
}
