using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public interface IAppIconBadge
{
    void SetUnreadChatCount(int count);
}

public class AppIconBadgeUpdater : WorkerBase
{
    private IServiceProvider Services { get; }

    public AppIconBadgeUpdater(IServiceProvider services)
        => Services = services;

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.WhenAll(
            UpdateAppIconBadge(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task UpdateAppIconBadge(CancellationToken cancellationToken)
    {
        var badge = Services.GetService<IAppIconBadge>();
        if (badge is null)
            return;

        var chatListUI = Services.GetRequiredService<ChatListUI>();
        var cChatsCount0 = await Computed
            .Capture(() => chatListUI.UnreadChatCount.Use(cancellationToken))
            .ConfigureAwait(false);
        var changes = cChatsCount0.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cChatsCount in changes.ConfigureAwait(false)) {
            if (cChatsCount.HasError)
                continue;

            var chatsCount = cChatsCount.Value;
            badge.SetUnreadChatCount(chatsCount.Value);
        }
    }
}
