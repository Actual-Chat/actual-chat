using _Microsoft.Android.Resource.Designer;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.UI.Blazor.Services;
using ActualLab.Generators;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using Android.Views;
using AndroidX.Core.App;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;

namespace ActualChat.App.Maui.Activities;

[Service(ForegroundServiceType =
    ForegroundService.TypeMediaPlayback |
    ForegroundService.TypeMicrophone |
    ForegroundService.TypeDataSync |
    ForegroundService.TypeLocation)]
public class AndroidActivitiesForegroundService : Service
{
    public static class IntentExtras
    {
        public const string Kind = nameof(ActivityKind);
        public const string ServiceTypes = nameof(ServiceTypes);
        public const string ChatId = nameof(ChatId);
        public const string ChatTitle = nameof(ChatTitle);
        public const string ChatPicUri = nameof(ChatPicUri);
        public const string ExtraChatCount = nameof(ExtraChatCount);
        public const string IsPaused = nameof(IsPaused);
        public const string CanPause = nameof(CanPause);
        public const string UploadFileCount = nameof(UploadFileCount);
        public const string UploadBytesUploaded = nameof(UploadBytesUploaded);
        public const string UploadTotalBytes = nameof(UploadTotalBytes);
        public const string UploadItemNames = nameof(UploadItemNames);
        public const string UploadItemSizes = nameof(UploadItemSizes);
        public const string UploadItemProgresses = nameof(UploadItemProgresses);
    }

    public const string ActionShow = "ACTION_SHOW";
    public const string ActionStop = "ACTION_STOP";
    private const string ChannelId = "audio_widget";
    private const string RecordingChannelId = "audio_recording";
    private const string RecordingQuietChannelId = "audio_recording_quiet";
    private const string LocationChannelId = "location_sharing";
    private const int NotificationId = 3001;
    private static ILogger Log { get; } = StaticLog.For<AndroidActivitiesForegroundService>();
    private static int _pendingStartCount;
    private static bool _isStopPending;
    private static int _lastRequestedTypes;
    private string _requestId = "";
    private MediaSessionCompat? _mediaSession;
    private string _lastTitle = "";
    private string _lastLink = "";
    private Bitmap? _lastAlbumArt;
    private Android.App.Notification? _lastNotification;
    private int _lastKind = -1;
    private bool _hasMicType;
    private Action<bool>? _micCapabilityHandler;
    private Action? _micBlockedHandler;
    private bool _isStopping;

    public static void TryStartArmed(Context context)
    {
        // Called from MainActivity while it's visible, which is the whole point: Android only grants
        // the microphone service type when the start happens with the app in the foreground, and the
        // backend that would otherwise raise this service arrives seconds after Blazor renders.
        var intent = new Intent(context, typeof(AndroidActivitiesForegroundService));
        intent.SetAction(ActionShow);
        intent.PutExtra(IntentExtras.Kind, (int)ActivityKind.Armed);
        intent.PutExtra(
            IntentExtras.ServiceTypes, (int)(ActivityServiceTypes.Playback | ActivityServiceTypes.Microphone));
        // Without this the backend's _isShown stays false while the service is genuinely running,
        // and HideImpl - which early-returns on it - can never take the armed notification down.
        if (TryStart(context, intent))
            AndroidActivitiesBackend.MarkForegroundServiceShown();
    }

    public static bool TryStart(Context context, Intent intent)
    {
        // OnStartRequested first: StartForegroundService dispatches OnStartCommand on the main
        // thread, so a caller running off it would otherwise register the start too late and leave
        // _pendingStartCount stuck above zero, deferring every later Stop() forever.
        var wasStopPending = Volatile.Read(ref _isStopPending);
        OnStartRequested();
        try {
            context.StartForegroundService(intent);
            return true;
        }
        catch (Exception e) {
            // The start OnStartRequested cancelled the deferred stop for never happened, so both
            // must go back - otherwise nothing can ever take the foreground notification down.
            if (Volatile.Read(ref _pendingStartCount) > 0)
                Interlocked.Decrement(ref _pendingStartCount);
            if (wasStopPending)
                Volatile.Write(ref _isStopPending, true);
            // Starting a mic FGS from the background is blocked (ForegroundServiceStartNotAllowedException):
            // this surfaces if the accept-over-lock-screen path lacks a foreground-visible activity.
            Log.LogError(e, "StartForegroundService failed");
            return false;
        }
    }

    public static void OnStartRequested()
    {
        // A new Show cancels a stop that's still waiting for the service to reach foreground.
        Volatile.Write(ref _isStopPending, false);
        Interlocked.Increment(ref _pendingStartCount);
    }

    public static void Stop(Context context)
    {
        // Never while a chat is armed. This service is what holds the microphone grant, and Android
        // only hands that to a service started with the app in the foreground - so stopping it from
        // a background path (a headless session tearing down, a wake failing) costs every later
        // walkie reply its mic, with no way to earn it back until MainActivity runs again.
        if (MauiPreferences.IsWalkieArmed) {
            Log.LogInformation("Stop: keeping the foreground service - walkie-talkie is armed");
            return;
        }

        // StopService() while a StartForegroundService() request hasn't reached OnStartCommand yet makes
        // Android kill the process with ForegroundServiceDidNotStartInTimeException, even though
        // OnStartCommand does call StartForeground(). Let the service stop itself in that case.
        if (Volatile.Read(ref _pendingStartCount) > 0) {
            Volatile.Write(ref _isStopPending, true);
            return;
        }

        var intent = new Intent(context, typeof(AndroidActivitiesForegroundService));
        context.StopService(intent);
    }

    public override void OnCreate()
    {
        Log.LogDebug("OnCreate");
        base.OnCreate();
        CreateNotificationChannel();
        NotificationHelper.EnsureActivityChannelsExist(this);
        _micCapabilityHandler = OnMicCapabilityRequested;
        WalkieTalkieMicCapability.SetHandler(_micCapabilityHandler);
        _micBlockedHandler = MicrophoneBlockedNotification.ShowPermissionDenied;
        WalkieTalkieMicCapability.SetBlockedHandler(_micBlockedHandler);
    }

    public override void OnDestroy()
    {
        Log.LogDebug("OnDestroy");
        Volatile.Write(ref _isStopping, true);
        if (_micCapabilityHandler is { } micCapabilityHandler) {
            WalkieTalkieMicCapability.ResetHandler(micCapabilityHandler);
            _micCapabilityHandler = null;
        }
        if (_micBlockedHandler is { } micBlockedHandler) {
            WalkieTalkieMicCapability.ResetBlockedHandler(micBlockedHandler);
            _micBlockedHandler = null;
        }
        _requestId = RandomStringGenerator.Default.Next();
        Interlocked.Exchange(ref _pendingStartCount, 0);
        Volatile.Write(ref _isStopPending, false);
        ReleaseMediaSession();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _requestId = RandomStringGenerator.Default.Next();
        var action = intent?.Action ?? "";
        if (action == ActionStop) {
            // The notification's Stop button - the same door the media session's OnStop takes.
            AndroidActivitiesBackend.Stop();
            return StartCommandResult.NotSticky;
        }

        if (action != ActionShow) {
            // An unknown/null action means AMS revived us without a real request (e.g. after the
            // process was killed) - there's nothing to show, and re-raising is MainActivity's job
            // (TryStartArmed), so take the notification down rather than sit in the foreground forever.
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // A Show revives an instance whose deferred stop never reached OnDestroy - otherwise the
        // microphone re-type would stay dead for the rest of its life.
        Volatile.Write(ref _isStopping, false);
        var kind = (ActivityKind)(intent!.Extras?.GetInt(IntentExtras.Kind) ?? 0);
        if (!Enum.IsDefined(kind))
            kind = ActivityKind.Listening;
        var requested = (ActivityServiceTypes)(intent.Extras?.GetInt(IntentExtras.ServiceTypes) ?? 0);

        // Android requires StartForeground() within ~5s of StartForegroundService(). Call it up-front
        // with a placeholder so a throw while building the rich notification (unexpected kind,
        // ChatId.Parse) can't leave the service without one -> ForegroundServiceDidNotStartInTimeException.
        StartForeground1(BuildStartingNotification(kind), kind, requested);
        if (Volatile.Read(ref _pendingStartCount) > 0)
            Interlocked.Decrement(ref _pendingStartCount);

        if (Volatile.Read(ref _isStopPending) && Volatile.Read(ref _pendingStartCount) == 0) {
            // Stop() deferred to us: StartForeground() above satisfied Android, so we can go away now.
            Volatile.Write(ref _isStopPending, false);
            Volatile.Write(ref _isStopping, true);
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (kind is ActivityKind.Uploading)
            return ShowUpload(intent, requested);
        if (kind is ActivityKind.SharingLocation)
            return ShowLocation(intent, requested);

        var chatTitle = intent.Extras!.GetString(IntentExtras.ChatTitle) ?? "Unknown chat";
        var chatSid = intent.Extras!.GetString(IntentExtras.ChatId);
        if (chatSid.IsNullOrEmpty()) {
            // The early armed start carries no chat yet - see TryStartArmed. StartForeground1 above
            // already claimed the microphone type, and the backend swaps in the real notification.
            // NotSticky: MainActivity re-raises via TryStartArmed on next launch, so an AMS revival
            // of this bare intent has no value.
            return StartCommandResult.NotSticky;
        }

        var chatPicUrl = intent.Extras!.GetString(IntentExtras.ChatPicUri) ?? "";
        var extraChatCount = intent.Extras!.GetInt(IntentExtras.ExtraChatCount);
        var isPaused = intent.Extras!.GetBoolean(IntentExtras.IsPaused);
        var canPause = intent.Extras!.GetBoolean(IntentExtras.CanPause);

        if (_mediaSession is null) {
            _mediaSession = new MediaSessionCompat(this, "AudioWidgetSession") { Active = true };
#pragma warning disable CS0618
            _mediaSession.SetFlags(
                MediaSessionCompat.FlagHandlesMediaButtons
                | MediaSessionCompat.FlagHandlesTransportControls);
#pragma warning restore CS0618
            _mediaSession.SetCallback(new Callback());
        }

        var text = GetKindText(kind);
        var title = chatTitle;
        if (extraChatCount > 0)
            title += extraChatCount == 1 ? " (+ 1 chat)" : $" (+ {extraChatCount} chats)";
        var chatId = ChatId.Parse(chatSid);
        var link = Links.Chat(chatId);

        long capabilities = 0;
        if (kind is ActivityKind.Replaying or ActivityKind.Listening) {
            if (canPause)
                capabilities |= isPaused ? PlaybackStateCompat.ActionPlay : PlaybackStateCompat.ActionPause;
            capabilities |= PlaybackStateCompat.ActionStop;
        }
        // Armed reports Paused, not Playing: nothing is playing, and the system media UI animates a
        // Playing session - a permanent "something is playing" pulse for an idle walkie. Paused
        // still keeps the session alive for the headset button, which Stopped would not.
        var isIdle = kind is ActivityKind.Armed
            || (kind is (ActivityKind.Replaying or ActivityKind.Listening) && isPaused);
        var playbackStateCompat = new PlaybackStateCompat.Builder()
            .SetState(
                isIdle ? PlaybackStateCompat.StatePaused : PlaybackStateCompat.StatePlaying,
                PlaybackStateCompat.PlaybackPositionUnknown,
                1.0f)!
            .SetActions(capabilities)!
            .Build();
        _mediaSession.SetPlaybackState(playbackStateCompat);

        var lastRequestId = _requestId;
        ResolveBitmapAndRun(
            chatPicUrl,
            bitmap => {
                // Callback is called twice when bitmap is loaded asynchronously:
                // first with null to ensure StartForeground() is called in time,
                // then with the actual bitmap to update the notification.
                if (!Equals(lastRequestId, _requestId))
                    return;

                // Kept so a microphone-capability change can re-render the same notification with
                // the new label instead of leaving "Listening" on screen while the mic is open.
                _lastTitle = title;
                _lastAlbumArt = bitmap;
                _lastLink = link;
                SetMetadata(title, text, bitmap);
                var notification = BuildNotification(_mediaSession, link, kind);
                StartForeground1(notification, kind, requested);
            });

        return StartCommandResult.NotSticky;
    }

    // Private methods

    private StartCommandResult ShowUpload(Intent intent, ActivityServiceTypes requested)
    {
        // Uploading only ever becomes primary when no audio activity is - see ActivitiesBackend -
        // so the media session has nothing left to serve.
        ReleaseMediaSession();
        var fileCount = intent.Extras!.GetInt(IntentExtras.UploadFileCount);
        var bytesUploaded = intent.Extras.GetLong(IntentExtras.UploadBytesUploaded);
        var totalBytes = intent.Extras.GetLong(IntentExtras.UploadTotalBytes);
        var names = intent.Extras.GetStringArray(IntentExtras.UploadItemNames) ?? [];
        var sizes = intent.Extras.GetLongArray(IntentExtras.UploadItemSizes) ?? [];
        var progresses = intent.Extras.GetLongArray(IntentExtras.UploadItemProgresses) ?? [];

        var items = ImmutableList.CreateBuilder<UploadActivityItem>();
        for (var i = 0; i < names.Length; i++)
            items.Add(new UploadActivityItem(
                "",
                names[i],
                i < progresses.Length ? progresses[i] : 0,
                i < sizes.Length ? sizes[i] : 0));
        var upload = new UploadActivity(fileCount, bytesUploaded, totalBytes, items.ToImmutable());
        var notification = NotificationHelper.BuildUploadNotification(this, upload);
        StartForeground1(notification, ActivityKind.Uploading, requested);
        return StartCommandResult.NotSticky;
    }

    private StartCommandResult ShowLocation(Intent intent, ActivityServiceTypes requested)
    {
        // Location only ever becomes primary when no audio activity is - the media session has
        // nothing left to serve.
        ReleaseMediaSession();
        var chatSid = intent.Extras!.GetString(IntentExtras.ChatId) ?? "";
        var chatTitle = intent.Extras.GetString(IntentExtras.ChatTitle) ?? "";
        var extraChatCount = intent.Extras.GetInt(IntentExtras.ExtraChatCount);
        var notification = BuildLocationNotification(chatSid, chatTitle, extraChatCount);
        StartForeground1(notification, ActivityKind.SharingLocation, requested);
        return StartCommandResult.NotSticky;
    }

    private void OnMicCapabilityRequested(bool isMicrophoneNeeded)
    {
        // Android grants while-in-use microphone access on the serviceType of the last
        // startForeground call, not on the [Service] attribute - and a wake starts as
        // mediaPlayback only, so a press must re-issue this before the mic is opened.
        // Inline on the main thread, because the media-button dispatch runs there and the raise
        // must stay inside it; BeginInvokeOnMainThread posts even from the main thread.
        var kind = isMicrophoneNeeded ? ActivityKind.Recording : ActivityKind.Listening;
        if (MainThread.IsMainThread)
            Apply();
        else
            BeginDispatchToMainThread(Apply);
        return;

        void Apply() {
            if (Volatile.Read(ref _isStopping) || Volatile.Read(ref _lastKind) == (int)kind)
                return;

            // Re-labelled, not re-posted as-is: reusing it left "Listening" on screen with the
            // microphone open.
            var requested = (ActivityServiceTypes)Volatile.Read(ref _lastRequestedTypes);
            if (isMicrophoneNeeded)
                requested |= ActivityServiceTypes.Microphone;
            // Falls back to a freshly built notification rather than the previous one: the armed
            // idle case has no media session to relabel, and reposting the low-importance armed
            // notification would leave nothing on the lock screen at the moment the mic opens.
            StartForeground1(Relabel(kind) ?? BuildStartingNotification(kind), kind, requested);
        }
    }

    private static string GetKindText(ActivityKind kind)
        => kind switch {
            ActivityKind.Recording => "Recording",
            ActivityKind.Listening => "Listening",
            ActivityKind.Replaying => "Replaying",
            ActivityKind.Armed => "Walkie-talkie is on",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private void SetMetadata(string title, string text, Bitmap? albumArt)
        => _mediaSession?.SetMetadata(new MediaMetadataCompat.Builder()
            .PutString(MediaMetadataCompat.MetadataKeyTitle, title)!
            .PutString(MediaMetadataCompat.MetadataKeyArtist, text)!
            .PutBitmap(MediaMetadataCompat.MetadataKeyAlbumArt, albumArt)!
            .Build());

    private Android.App.Notification? Relabel(ActivityKind kind)
    {
        // Null until a real activity has been rendered - the bare armed start carries no chat, so
        // there's nothing to relabel and the caller keeps whatever is already on screen.
        if (_mediaSession is null || _lastTitle.IsNullOrEmpty())
            return null;
        if (kind is not (ActivityKind.Recording or ActivityKind.Listening or ActivityKind.Replaying))
            return null;

        try {
            SetMetadata(_lastTitle, GetKindText(kind), _lastAlbumArt);
            return BuildNotification(_mediaSession, _lastLink, kind);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't relabel the notification for {Kind}", kind);
            return null;
        }
    }

    private void StartForeground1(
        Android.App.Notification notification, ActivityKind kind, ActivityServiceTypes requested)
    {
        try {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) {
                // The mic type latches: Android re-evaluates the while-in-use grant on every
                // startForeground, and every call after the first one happens with the app in the
                // background - so dropping the type once would cost the grant for good. The location
                // type is driven by the set instead: it stays for the life of the share (new shares
                // only start from the foreground UI, so re-raising it later is always legitimate).
                var serviceType = ToForegroundServiceType(requested);
                var mustHaveMic = (requested & ActivityServiceTypes.Microphone) != 0
                    || Volatile.Read(ref _hasMicType);
                if (mustHaveMic)
                    serviceType |= ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback;
                if (serviceType == 0)
                    serviceType = ForegroundService.TypeMediaPlayback;
                StartForeground(NotificationId, notification, serviceType);
                Volatile.Write(ref _hasMicType, mustHaveMic);
            }
            else
                StartForeground(NotificationId, notification);
            Volatile.Write(ref _lastNotification, notification);
            Volatile.Write(ref _lastKind, (int)kind);
            Volatile.Write(ref _lastRequestedTypes, (int)requested);
        }
        catch (Exception e) {
            // A mic FGS started over the keyguard can be rejected (SecurityException /
            // ForegroundServiceStartNotAllowedException) on some OEMs — log rather than crash.
            Log.LogError(e, "StartForeground failed (kind={Kind})", kind);
            // A wake-revived process never was foreground, so it can't earn the while-in-use
            // microphone and this reply is already lost - tell the user while they're still holding
            // the phone, rather than leaving them with cues that promised a recording.
            if (kind is ActivityKind.Recording)
                MicrophoneBlockedNotification.ShowUnavailable();
        }
    }

    private static ForegroundService ToForegroundServiceType(ActivityServiceTypes types)
    {
        ForegroundService result = 0;
        if ((types & ActivityServiceTypes.Playback) != 0)
            result |= ForegroundService.TypeMediaPlayback;
        if ((types & ActivityServiceTypes.Microphone) != 0)
            result |= ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback;
        if ((types & ActivityServiceTypes.Location) != 0)
            result |= ForegroundService.TypeLocation;
        if ((types & ActivityServiceTypes.DataSync) != 0)
            result |= ForegroundService.TypeDataSync;
        return result;
    }

    private static void ResolveBitmapAndRun(string uri, Action<Bitmap?> callback)
    {
        if (uri.IsNullOrEmpty()) {
            callback(null);
            return;
        }

        var bitmapTask = NotificationHelper.GetImageAsync(uri);
        if (bitmapTask.IsCompleted) {
            if (!bitmapTask.IsCompletedSuccessfully)
                callback(null);
            else
                callback(bitmapTask.GetAwaiter().GetResult());
            return;
        }

        // Call callback immediately with null to ensure StartForeground() is called in time.
        // Android requires StartForeground() within 5-10 seconds of startForegroundService().
        callback(null);

        _ = bitmapTask.ContinueWith(t => {
            if (!t.IsCompletedSuccessfully)
                return;

            var bitmap = bitmapTask.GetAwaiter().GetResult();
            BeginDispatchToMainThread(() => {
                // Update notification with the loaded bitmap
                callback(bitmap);
            });
        }, TaskScheduler.Default);
    }

    private Android.App.Notification BuildStartingNotification(ActivityKind kind)
        // Carries real text because the armed start (TryStartArmed) has no chat to name yet and
        // this notification is all the user sees until the backend arrives, which takes as long as
        // Blazor needs to render. A blank one reads as "walkie-talkie isn't running".
        => new NotificationCompat.Builder(this, GetChannelId(kind))
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentTitle(kind switch {
                ActivityKind.Armed => "Walkie-talkie is on",
                ActivityKind.Recording => "Recording",
                ActivityKind.Uploading => "Uploading",
                ActivityKind.SharingLocation => "Sharing live location",
                _ => "Voxt",
            })!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetVisibility(NotificationCompat.VisibilityPublic)!
            .Build()!;

    private Android.App.Notification BuildNotification(MediaSessionCompat mediaSession, string link, ActivityKind kind)
    {
        var viewIntent = NotificationHelper.CreateViewIntent(this, link);
        var viewPending = PendingIntent.GetActivity(this, 3, viewIntent, PendingIntentFlags.Immutable);

        var stopIntent = new Intent(this, typeof(AndroidActivitiesForegroundService));
        stopIntent.SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 4, stopIntent, PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, GetChannelId(kind))
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentIntent(viewPending)!
            .SetOngoing(true)!
            // One banner per session: a Listening <-> Recording flip re-posts this same
            // notification, and each re-post would otherwise pop over the keyguard again.
            .SetOnlyAlertOnce(true)!
            // Public so an open microphone is still legible over the keyguard - that's exactly
            // when the user needs to see it, and it names only a chat they're already in.
            .SetVisibility(NotificationCompat.VisibilityPublic)!
            .AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", stopPending)!;

        var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetMediaSession(mediaSession.SessionToken)!
            .SetShowActionsInCompactView(0)!;
        builder.SetStyle(mediaStyle);

        return builder.Build()!;
    }

    private Android.App.Notification BuildLocationNotification(string chatSid, string chatTitle, int extraChatCount)
    {
        var stopIntent = new Intent(this, typeof(AndroidActivitiesForegroundService));
        stopIntent.SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 4, stopIntent, PendingIntentFlags.Immutable);

        // Opening the shared chat beats opening the app, but an unparsable id must still open something.
        var link = ChatId.TryParse(chatSid, out var chatId) ? Links.Chat(chatId) : Links.Home;
        var viewIntent = NotificationHelper.CreateViewIntent(this, link);
        var viewPending = viewIntent is null
            ? null
            : PendingIntent.GetActivity(this, 3, viewIntent, PendingIntentFlags.Immutable);

        var text = chatTitle;
        if (!text.IsNullOrEmpty() && extraChatCount > 0)
            text += extraChatCount == 1 ? " (+ 1 chat)" : $" (+ {extraChatCount} chats)";

        var builder = new NotificationCompat.Builder(this, LocationChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentTitle("Sharing live location")!
            .SetOngoing(true)!
            .AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", stopPending)!;
        if (!text.IsNullOrEmpty())
            _ = builder.SetContentText(text);
        if (viewPending is not null)
            _ = builder.SetContentIntent(viewPending);
        return builder.Build()!;
    }

    private void ReleaseMediaSession()
    {
        if (_mediaSession is null)
            return;

        _mediaSession.Active = false;
        _mediaSession.Release();
        _mediaSession.DisposeSilently();
        _mediaSession = null;
    }

    private void CreateNotificationChannel()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(
            new NotificationChannel(ChannelId, "Audio Widget", NotificationImportance.Low));
        manager.CreateNotificationChannel(
            new NotificationChannel(LocationChannelId, "Location sharing", NotificationImportance.Low));
        // High so an open microphone surfaces over the keyguard instead of sitting in the shade,
        // silent because the walkie's own cues already say it started - this is the visual half.
        var recordingChannel = new NotificationChannel(
            RecordingChannelId, "Recording alerts", NotificationImportance.High);
        recordingChannel.SetSound(null, null);
        recordingChannel.EnableVibration(false);
        recordingChannel.LockscreenVisibility = NotificationVisibility.Public;
        manager.CreateNotificationChannel(recordingChannel);
        // A channel's importance is fixed when it's created - it can't be lowered later, and the
        // High one is already out there - so the no-banner case needs a channel of its own.
        var quietRecordingChannel = new NotificationChannel(
            RecordingQuietChannelId, "Recording", NotificationImportance.Low);
        quietRecordingChannel.SetSound(null, null);
        quietRecordingChannel.EnableVibration(false);
        quietRecordingChannel.LockscreenVisibility = NotificationVisibility.Public;
        manager.CreateNotificationChannel(quietRecordingChannel);
    }

    private static bool TryHandleHeadsetButton(HeadsetKey key, bool isDown, int repeatCount)
    {
        // Runs on the main thread: SetCallback binds its Handler to the Looper of the thread that
        // called it, which is OnStartCommand's. So this must neither block nor throw - a throw
        // would escape the media-button dispatch instead of reaching the base callback, e.g.
        // GetRequiredService on a scope that's concurrently being disposed.
        try {
            if (AppScopeAccessor.Current is not { } services)
                return false;

            var hub = services.GetRequiredService<AppUIHub>();
            var state = hub.GestureUI.GetHeadsetButtonState();
            var action = HeadsetButtonPolicy.Decide(
                key, isDown, repeatCount, state.IsEnabled,
                state.HasAnswerWindow, state.IsReplyHot, state.IsPracticeMode);
            if (action == HeadsetButtonAction.PassThrough)
                return false;

            // The hold is taken synchronously inside the media-button dispatch, which is where
            // Android hands out the while-in-use exemption a background mic start needs, and it is
            // released when the trigger ends - a reply that never opened can't leave it raised.
            var replyUI = hub.WalkieTalkieReplyUI;
            var whenHandled = action == HeadsetButtonAction.StopReply
                ? replyUI.StopReply()
                : WalkieTalkieMicCapability.HoldWhile(() => replyUI.RequestReply(CancellationToken.None));
            _ = BackgroundTask.Run(() => whenHandled, Log, $"{action} from the headset button failed",
                CancellationToken.None);
            return true;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Headset button handling failed");
            return false;
        }
    }

    private static KeyEvent? GetKeyEvent(Intent? mediaButtonEvent)
    {
        if (mediaButtonEvent is null)
            return null;

        var extra = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? mediaButtonEvent.GetParcelableExtra(Intent.ExtraKeyEvent, Java.Lang.Class.FromType(typeof(KeyEvent)))
#pragma warning disable CA1422
            : mediaButtonEvent.GetParcelableExtra(Intent.ExtraKeyEvent);
#pragma warning restore CA1422
        return extra as KeyEvent;
    }

    private static string GetChannelId(ActivityKind kind)
        => kind switch {
            ActivityKind.Uploading => NotificationHelper.Constants.ActivityUploadChannelId,
            ActivityKind.SharingLocation => LocationChannelId,
            // The banner exists for the case the user can't see the app; with the app in front of
            // them it lands on top of the UI that's already showing the recording, and Android's
            // own microphone indicator is up as well.
            ActivityKind.Recording => AndroidUtils.IsAppForeground() ?? false
                ? RecordingQuietChannelId
                : RecordingChannelId,
            _ => ChannelId,
        };

    // Nested types

    private class Callback : MediaSessionCompat.Callback
    {
        public override bool OnMediaButtonEvent(Intent? mediaButtonEvent)
        {
            var keyEvent = GetKeyEvent(mediaButtonEvent);
            if (keyEvent is null)
                return base.OnMediaButtonEvent(mediaButtonEvent);

            var key = keyEvent.KeyCode switch {
                Keycode.Headsethook => HeadsetKey.Hook,
                Keycode.MediaPlayPause => HeadsetKey.PlayPause,
                _ => HeadsetKey.Unknown,
            };
            if (key == HeadsetKey.Unknown)
                return base.OnMediaButtonEvent(mediaButtonEvent);

            var isDown = keyEvent.Action == KeyEventActions.Down;
            if (!TryHandleHeadsetButton(key, isDown, keyEvent.RepeatCount))
                return base.OnMediaButtonEvent(mediaButtonEvent);

            return true;
        }

        public override void OnPlay()
            => AndroidActivitiesBackend.Resume();

        public override void OnPause()
            => AndroidActivitiesBackend.Pause();

        public override void OnStop()
            => AndroidActivitiesBackend.Stop();
    }
}
