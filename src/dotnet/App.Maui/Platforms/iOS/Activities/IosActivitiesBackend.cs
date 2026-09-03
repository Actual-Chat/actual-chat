using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;
using Foundation;
using Microsoft.Extensions.Localization;
using UserNotifications;

namespace ActualChat.App.Maui.Activities;

/// <summary>
/// iOS <see cref="ActivitiesBackend"/>: per-domain keep-alives (AVAudioSession for audio,
/// location background mode, BeginBackgroundTask for uploads) plus one Live Activity
/// rendering the primary activity; a notification fallback covers upload progress
/// when Live Activities are unavailable.
/// </summary>
public sealed class IosActivitiesBackend : ActivitiesBackend
{
    private const string UploadNotificationId = "activities.upload";
    private readonly IosUploadKeepAlive _uploadKeepAlive;
    private readonly ActivitiesUI _activitiesUI;
    private ActivitySet? _lastRendered;
    private bool _isLiveActivityShown;
    private IStringLocalizer L => Hub.StringLocalizer;

    public IosActivitiesBackend(AppUIHub hub, IosUploadKeepAlive uploadKeepAlive) : base(hub)
    {
        _uploadKeepAlive = uploadKeepAlive;
        _activitiesUI = hub.Services.GetRequiredService<ActivitiesUI>();
        // A Live Activity start from the background is rejected (PTT wake, upload finishing
        // late); re-render the last state when the app comes back to the foreground.
        _activitiesUI.IsBackground.Updated += OnBackgroundStateUpdated;
    }

    public override void Dispose()
    {
        _activitiesUI.IsBackground.Updated -= OnBackgroundStateUpdated;
        base.Dispose();
    }

    protected override void OnStateChanged(ActivitySet? state, ActivitySet? oldState)
    {
        _lastRendered = state;
        // Re-read _lastRendered inside the dispatched lambda rather than capturing state: a
        // background-foreground trigger (below) may enqueue its own Render on the same main-thread
        // queue, and re-reading keeps whichever lambda runs last acting on the latest value instead
        // of resurrecting a stale one.
        BeginDispatchToMainThread(() => Render(_lastRendered));
    }

    // Private methods

    private void OnBackgroundStateUpdated(State state, StateEventKind eventKind)
    {
        if (eventKind != StateEventKind.Updated || ((IState<bool>)state).Value)
            return;

        BeginDispatchToMainThread(() => {
            if (_lastRendered is not null && !_isLiveActivityShown)
                Render(_lastRendered);
        });
    }

    private void Render(ActivitySet? state)
    {
        // Upload keep-alive: active while any upload runs, primary or not.
        if (state is not null && state.Contains(ActivityKind.Uploading))
            _uploadKeepAlive.Begin("activities.upload");
        else
            _uploadKeepAlive.End();

        var isEnabled = IosLiveActivities.AreEnabled() == 1;
        if (isEnabled)
            RenderLiveActivity(state);
        UpdateFallbackNotification(isEnabled ? null : state);
    }

    private void RenderLiveActivity(ActivitySet? state)
    {
        if (state?.Primary is not { } primary) {
            if (_isLiveActivityShown)
                IosLiveActivities.End();
            _isLiveActivityShown = false;
            return;
        }

        var (title, subtitle, progress) = primary switch {
            AudioActivity audio => (audio.Chat.Title, KindLabel(audio), -1.0),
            LocationActivity location => (
                L.Activity_SharingLocation,
                location.Chat.ExtraChatCount > 0
                    ? L.Activity_Chats(location.Chat.ExtraChatCount + 1, location.Chat.ExtraChatCount + 1)
                    : location.Chat.Title,
                -1.0),
            UploadActivity upload => (
                L.Upload_UploadingFiles(upload.FileCount, upload.FileCount),
                $"{FormatBytes(upload.BytesUploaded)} / {FormatBytes(upload.TotalBytes)}",
                upload.Progress),
            _ => (primary.Kind.ToString(), "", -1.0),
        };
        _isLiveActivityShown =
            IosLiveActivities.StartOrUpdate((int)primary.Kind, title, subtitle, progress) == 1;
    }

    private void UpdateFallbackNotification(ActivitySet? state)
    {
        // Audio kinds post nothing: AVAudioSession already keeps the app alive, and a PTT
        // session is Apple's Ptt UI - a second banner for it is noise.
        var center = UNUserNotificationCenter.Current;
        if (state?.Primary is not UploadActivity upload) {
            Remove(center, UploadNotificationId);
            return;
        }

        var content = new UNMutableNotificationContent {
            Title = new NSString(L.Upload_UploadingFiles(upload.FileCount, upload.FileCount)),
            Body = new NSString(
                $"{FormatBytes(upload.BytesUploaded)} / {FormatBytes(upload.TotalBytes)}"
                + $" ({(int)(upload.Progress * 100)}%)"),
            ThreadIdentifier = "activities",
            // Progress re-posts this notification under the same identifier, which updates it in
            // place - Passive keeps those updates from lighting up the screen each time.
            InterruptionLevel = UNNotificationInterruptionLevel.Passive,
        };
        var request = UNNotificationRequest.FromIdentifier(UploadNotificationId, content, null);
        center.AddNotificationRequest(request, null);
    }

    private string KindLabel(AudioActivity audio)
        // Armed says whether a flip would actually do something rather than just that a chat is
        // armed: gestures work on iOS too, and outside the arming window the accelerometer is
        // stopped, so "push-to-talk is on" would promise one that cannot fire.
        => audio.Kind switch {
            ActivityKind.Recording => L.Activity_Recording,
            ActivityKind.Replaying => L.Activity_Replaying,
            ActivityKind.Listening => L.Activity_Listening,
            ActivityKind.Armed => audio.IsStartGestureReady
                ? L.Activity_FlipToReply
                : L.Activity_PttOn,
            _ => "",
        };

    private static void Remove(UNUserNotificationCenter center, params string[] ids)
    {
        center.RemovePendingNotificationRequests(ids);
        center.RemoveDeliveredNotifications(ids);
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024.0;
        const double mb = 1024.0 * 1024.0;
        const double gb = 1024.0 * 1024.0 * 1024.0;
        if (bytes < kb)
            return $"{bytes} B";
        if (bytes < mb)
            return $"{bytes / kb:0.#} KB";
        if (bytes < gb)
            return $"{bytes / mb:0.#} MB";

        return $"{bytes / gb:0.##} GB";
    }
}
