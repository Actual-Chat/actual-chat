using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using AndroidX.Core.App;
using IntentExtras = ActualChat.App.Maui.Activities.AndroidActivitiesForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Activities;

public class AndroidActivitiesBackend : ActivitiesBackend
{
    private static AndroidActivitiesBackend? _instance;
    private static bool _isShown;
    private static bool _isWakeOwned;
    private static int _lastIsArmed = -1;
    private static ILogger? _log;
    private bool _isDisposed;
    private static ILogger Log => _log ??= StaticLog.For(typeof(AndroidActivitiesBackend));
    private static Context Context => Platform.AppContext;

    public AndroidActivitiesBackend(AppUIHub hub) : base(hub)
    {
        Interlocked.Exchange(ref _instance, this);
        _ = DispatchToBlazor(_ => {
            if (IsStale())
                return;

            // MainActivity raises the armed service from OnCreate, and it's the only start that
            // happens with the app reliably visible - the one Android grants the microphone type
            // on. Hiding it here (a warm start leaves _isShown set from the previous scope) would
            // stop it, and the re-show would run from the background with the mic type lost.
            if (MauiPreferences.IsPttArmed)
                return;

            HideImpl();
        });
    }

    public override void Dispose()
    {
        // Published before _instance is cleared: a dispatch parked in a headless scope can resume
        // long after this scope died, and _instance may still point at it.
        Volatile.Write(ref _isDisposed, true);
        Interlocked.CompareExchange(ref _instance, null, this);
        base.Dispose();
    }

    public static void Pause()
    {
        // The wake session drives its own playback and offers no pause/resume, only Stop - and
        // nothing headless can re-issue ShowImpl to flip the button back to Play, so acting here
        // would strand the user on a paused stream behind a Pause button.
        if (HeadlessBlazorScope.Current is not null)
            return;

        Volatile.Read(ref _instance)?.InvokeAction(ActionNames.Pause);
    }

    public static void Resume()
    {
        if (HeadlessBlazorScope.Current is not null)
            return;

        Volatile.Read(ref _instance)?.InvokeAction(ActionNames.Resume);
    }

    public static void Stop()
    {
        // A headless wake session owns the FGS and the listening state, and can now have an
        // AndroidActivitiesBackend instance of its own - so the session decides who stops, not the
        // instance.
        if (HeadlessBlazorScope.Current is not null) {
            PttWakeHandler.StopHeadlessSession();
            return;
        }

        Volatile.Read(ref _instance)?.InvokeAction(ActionNames.Stop);
    }

    public static void Hide() => HideImpl();

    // Protected/internal methods

    protected override bool IsArmedPersisted => MauiPreferences.IsPttArmed;

    protected override void OnArmedChanged(bool isArmed)
    {
        // MainActivity reads this on the next launch, when the backend owning the state doesn't
        // exist yet - the backend only appears once Blazor has rendered, far too late for a
        // foreground service start to still count as one. Written on change only: this lands on
        // disk, and ComputeState runs orders of magnitude more often than the armed set changes.
        var value = isArmed ? 1 : 0;
        if (Interlocked.Exchange(ref _lastIsArmed, value) == value)
            return;

        MauiPreferences.IsPttArmed = isArmed;
    }

    protected override void OnStateChanged(ActivitySet? state, ActivitySet? oldState)
        => _ = DispatchToBlazor(_ => {
            if (IsStale())
                return;

            if (state is null)
                HideImpl();
            else
                ShowImpl(state);
        });

    internal static void MarkForegroundServiceShown()
    {
        // Ownership is claimable only while nothing is shown: once the WebView backend owns the
        // service, a failing wake must not be able to take it down - nothing would re-show it.
        if (!Volatile.Read(ref _isShown))
            Volatile.Write(ref _isWakeOwned, true);
        Volatile.Write(ref _isShown, true);
    }

    internal static void MarkForegroundServiceHidden()
    {
        Volatile.Write(ref _isShown, false);
        Volatile.Write(ref _isWakeOwned, false);
    }

    internal static bool IsForegroundServiceWakeOwned()
        => Volatile.Read(ref _isWakeOwned);

    // Private methods

    private void ShowImpl(ActivitySet set)
    {
        var context = Context;
        var intent = new Intent(context, typeof(AndroidActivitiesForegroundService));
        intent.SetAction(AndroidActivitiesForegroundService.ActionShow);
        var primary = set.Primary!;
        intent.PutExtra(IntentExtras.Kind, (int)primary.Kind);
        intent.PutExtra(IntentExtras.ServiceTypes, (int)set.GetServiceTypes());
        switch (primary) {
        case AudioActivity audio:
            intent.PutExtra(IntentExtras.IsPaused, audio.IsPaused);
            intent.PutExtra(IntentExtras.CanPause, audio.CanPause);
            intent.PutExtra(IntentExtras.ChatId, audio.Chat.Id.Value);
            intent.PutExtra(IntentExtras.ChatTitle, audio.Chat.Title);
            intent.PutExtra(IntentExtras.ChatPicUri, audio.Chat.PicUrl);
            intent.PutExtra(IntentExtras.ExtraChatCount, audio.Chat.ExtraChatCount);
            // Milliseconds-from-now rather than the Moment itself: the service compares against
            // the device wall clock, and the ServerClock stamp isn't in that domain.
            if (audio.AnswerWindowEndsAt is { } endsAt) {
                var remaining = endsAt - Hub.Clocks.ServerClock.Now;
                if (remaining > TimeSpan.Zero)
                    intent.PutExtra(
                        IntentExtras.AnswerWindowRemainingMs, (long)remaining.TotalMilliseconds);
            }
            break;
        case LocationActivity location:
            intent.PutExtra(IntentExtras.ChatId, location.Chat.Id.Value);
            intent.PutExtra(IntentExtras.ChatTitle, location.Chat.Title);
            intent.PutExtra(IntentExtras.ExtraChatCount, location.Chat.ExtraChatCount);
            break;
        case UploadActivity upload:
            PutUploadExtras(intent, upload);
            break;
        }
        if (AndroidActivitiesForegroundService.TryStart(context, intent)) {
            Volatile.Write(ref _isShown, true);
            // The backend's own state drives the service from here on, so a wake failure must not
            // take it down - nothing would ever re-show it.
            Volatile.Write(ref _isWakeOwned, false);
        }
        else
            Log.LogWarning("ShowImpl: couldn't start the FGS (kind={Kind})", primary.Kind);
        UpdateUploadNotification(set);
    }

    private static void UpdateUploadNotification(ActivitySet set)
    {
        // The FGS renders only the primary activity, so an upload running behind an audio or
        // location activity would otherwise never show its progress. It gets its own notification
        // in that case - and none when it IS primary, which would duplicate the FGS one.
        var upload = set.Primary is UploadActivity
            ? null
            : set.Activities.OfType<UploadActivity>().FirstOrDefault();
        if (upload is null) {
            CancelUploadNotification();
            return;
        }

        var context = Context;
        try {
            NotificationHelper.EnsureActivityChannelsExist(context);
            var notification = NotificationHelper.BuildUploadNotification(context, upload);
            NotificationManagerCompat.From(context)
                .Notify(NotificationHelper.Constants.UploadNotificationId, notification);
        }
        catch (Exception e) {
            // Posting needs POST_NOTIFICATIONS on API 33+; a denied permission must not take the
            // FGS down with it.
            Log.LogWarning(e, "Couldn't post the upload notification");
        }
    }

    private static void CancelUploadNotification()
    {
        try {
            NotificationManagerCompat.From(Context).Cancel(NotificationHelper.Constants.UploadNotificationId);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't cancel the upload notification");
        }
    }

    private static void HideImpl()
    {
        CancelUploadNotification();
        if (!Volatile.Read(ref _isShown))
            return;

        Volatile.Write(ref _isShown, false);
        Volatile.Write(ref _isWakeOwned, false);
        AndroidActivitiesForegroundService.Stop(Context);
    }

    private bool IsStale()
        => Volatile.Read(ref _isDisposed) || Volatile.Read(ref _instance) != this;

    private static void PutUploadExtras(Intent intent, UploadActivity upload)
    {
        intent.PutExtra(IntentExtras.UploadFileCount, upload.FileCount);
        intent.PutExtra(IntentExtras.UploadBytesUploaded, upload.BytesUploaded);
        intent.PutExtra(IntentExtras.UploadTotalBytes, upload.TotalBytes);
        var names = new string[upload.Items.Count];
        var sizes = new long[upload.Items.Count];
        var progresses = new long[upload.Items.Count];
        for (var i = 0; i < upload.Items.Count; i++) {
            names[i] = upload.Items[i].FileName;
            sizes[i] = upload.Items[i].TotalBytes;
            progresses[i] = upload.Items[i].BytesUploaded;
        }
        intent.PutExtra(IntentExtras.UploadItemNames, names);
        intent.PutExtra(IntentExtras.UploadItemSizes, sizes);
        intent.PutExtra(IntentExtras.UploadItemProgresses, progresses);
    }
}
