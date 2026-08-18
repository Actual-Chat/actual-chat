using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// Scoped watcher: joins the PTT channel while the user has Push-to-Talk chats
/// armed and leaves it when the last one is disarmed.
/// </summary>
public class IosPttUI : UIWorkerBase<AppUIHub>
{
    public IosPttUI(AppUIHub hub) : base(hub)
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        var cArmedChatIds = await Computed
            .Capture(() => chatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        // GetPttChatIds reads the whole UserPttSettings record, so its invalidation also
        // covers IsPttTransmitEnabled - see the same note in GestureUI.TrackActivation.
        await foreach (var change in cArmedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count == 0) {
                IosPtt.Leave();
                continue;
            }

            var settings = await UserSettingsUI.UserPttSettings()
                .Get(cancellationToken)
                .ConfigureAwait(false);
            IosPtt.SetTransmitEnabled(settings.IsPttTransmitEnabled ?? true);
            IosPtt.EnsureJoined();
        }
    }
}
