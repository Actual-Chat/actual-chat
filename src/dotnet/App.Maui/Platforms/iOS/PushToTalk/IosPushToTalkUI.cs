using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Interception;

namespace ActualChat.App.Maui;

/// <summary>
/// Scoped watcher: joins the PTT channel while the user has armed ("Keep listening")
/// chats and leaves it when the last one is disarmed.
/// </summary>
public class IosPushToTalkUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), INotifyInitialized
{
    void INotifyInitialized.Initialized()
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        var cArmedChatIds = await Computed
            .Capture(() => chatAudioUI.GetChatsYouNeedToKeepListeningTo(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var change in cArmedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count != 0)
                IosPushToTalk.EnsureJoined();
            else
                IosPushToTalk.Leave();
        }
    }
}
