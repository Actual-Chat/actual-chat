using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// Scoped watcher: joins the PTT channel while the user has Push-to-Talk chats
/// armed and leaves it when the last one is disarmed.
/// </summary>
public class IosPushToTalkUI : UIWorkerBase<AppUIHub>
{
    public IosPushToTalkUI(AppUIHub hub) : base(hub)
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        var cArmedChatIds = await Computed
            .Capture(() => chatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var change in cArmedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count != 0)
                IosPushToTalk.EnsureJoined();
            else
                IosPushToTalk.Leave();
        }
    }
}
