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
        // Joined = armed or muted: a mute must keep the channel (and the APNs PTT token) - a
        // background rejoin when it lapses is refused, and nothing would retry it.
        var cJoinedChatIds = await Computed
            .Capture(() => chatAudioUI.GetJoinedPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        // GetJoinedPttChatIds reads the whole UserPttSettings record, so its invalidation also
        // covers IsPttTransmitEnabled - see the same note in GestureUI.TrackActivation.
        await foreach (var change in cJoinedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count == 0) {
                IosPtt.Leave();
                continue;
            }

            var settings = await UserSettingsUI.UserPttSettings()
                .Get(cancellationToken)
                .ConfigureAwait(false);
            IosPtt.SetTransmitEnabled(settings.IsPttTransmitEnabled ?? true);
            // Asked for here, where arming is a deliberate act with the app up: a wake can't
            // prompt from a locked screen, and without the grant PTT can't tell Sleep Focus is on.
            IosFocusStatus.EnsureAuthorized();
            IosPtt.EnsureJoined();
        }
    }
}
