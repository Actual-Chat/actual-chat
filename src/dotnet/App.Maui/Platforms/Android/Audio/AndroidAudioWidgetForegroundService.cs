using _Microsoft.Android.Resource.Designer;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using Android.Views;
using AndroidX.Core.App;

namespace ActualChat.App.Maui.Audio;

[Service(ForegroundServiceType = ForegroundService.TypeMediaPlayback | ForegroundService.TypeMicrophone)]
public class AndroidAudioWidgetForegroundService : Service
{
    public static class IntentExtras
    {
        public const string Mode = nameof(AudioWidgetMode);
        public const string ChatId = nameof(ChatId);
        public const string ChatTitle = nameof(ChatTitle);
        public const string ChatPicUri = nameof(ChatPicUri);
        public const string ExtraChatCount = nameof(ExtraChatCount);
        public const string IsPaused = nameof(IsPaused);
        public const string CanPause = nameof(CanPause);
    }

    public const string ActionShow = "ACTION_SHOW";
    public const string ActionStop = "ACTION_STOP";
    private const string ChannelId = "audio_widget";
    private const int NotificationId = 3001;
    private static int _pendingStartCount;
    private static bool _isStopPending;
    private string _requestId = "";
    private MediaSessionCompat? _mediaSession;
    private Android.App.Notification? _lastNotification;
    private int _lastMode = -1;
    private Action<bool>? _micCapabilityHandler;
    private bool _isStopping;
    private static ILogger Log { get; } = StaticLog.For<AndroidAudioWidgetForegroundService>();

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
        // StopService() while a StartForegroundService() request hasn't reached OnStartCommand yet makes
        // Android kill the process with ForegroundServiceDidNotStartInTimeException, even though
        // OnStartCommand does call StartForeground(). Let the service stop itself in that case.
        if (Volatile.Read(ref _pendingStartCount) > 0) {
            Volatile.Write(ref _isStopPending, true);
            return;
        }

        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        context.StopService(intent);
    }

    public override void OnCreate()
    {
        Log.LogDebug("OnCreate");
        base.OnCreate();
        CreateNotificationChannel();
        _micCapabilityHandler = OnMicCapabilityRequested;
        WalkieTalkieMicCapability.SetHandler(_micCapabilityHandler);
    }

    public override void OnDestroy()
    {
        Log.LogDebug("OnDestroy");
        Volatile.Write(ref _isStopping, true);
        if (_micCapabilityHandler is { } micCapabilityHandler) {
            WalkieTalkieMicCapability.ResetHandler(micCapabilityHandler);
            _micCapabilityHandler = null;
        }
        _requestId = Guid.NewGuid().ToString();
        Interlocked.Exchange(ref _pendingStartCount, 0);
        Volatile.Write(ref _isStopPending, false);
        if (_mediaSession is not null) {
            _mediaSession.Active = false;
            _mediaSession.Release();
            _mediaSession.DisposeSilently();
            _mediaSession = null;
        }
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _requestId = Guid.NewGuid().ToString();
        var action = intent?.Action ?? "";
        if (action == ActionStop) {
            // The notification's Stop button - the same door the media session's OnStop takes.
            AndroidAudioWidget.Stop();
            return StartCommandResult.NotSticky;
        }

        if (action != ActionShow)
            return StartCommandResult.Sticky;

        var mode = (AudioWidgetMode)(intent!.Extras?.GetInt(IntentExtras.Mode) ?? 0);
        if (!Enum.IsDefined(mode))
            mode = AudioWidgetMode.Listening;
        // Android requires StartForeground() within ~5s of StartForegroundService(). Call it up-front
        // with a placeholder so a throw while building the rich notification (unexpected mode,
        // ChatId.Parse) can't leave the service without one -> ForegroundServiceDidNotStartInTimeException.
        StartForeground1(BuildStartingNotification(), mode);
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

        var chatTitle = intent.Extras!.GetString(IntentExtras.ChatTitle) ?? "Unknown chat";
        var chatSid = intent.Extras!.GetString(IntentExtras.ChatId);
        var chatPicUrl = intent.Extras!.GetString(IntentExtras.ChatPicUri) ?? "";
        var extraChatCount = intent.Extras!.GetInt(IntentExtras.ExtraChatCount);
        var isPaused = intent.Extras!.GetBoolean(IntentExtras.IsPaused);
        var canPause = intent.Extras!.GetBoolean(IntentExtras.CanPause);

        if (_mediaSession is null) {
            _mediaSession = new MediaSessionCompat(this, "AudioWidgetSession") { Active = true };
#pragma warning disable CS0618
            // Type or member is obsolete
            _mediaSession.SetFlags(
                MediaSessionCompat.FlagHandlesMediaButtons
                | MediaSessionCompat.FlagHandlesTransportControls);
#pragma warning restore CS0618
            // Type or member is obsolete
            _mediaSession.SetCallback(new Callback());
        }

        var text = mode switch {
            AudioWidgetMode.Recording => "Recording",
            AudioWidgetMode.Listening => "Listening",
            AudioWidgetMode.Replaying => "Replaying",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var title = chatTitle;
        if (extraChatCount > 0)
            title += extraChatCount == 1 ? " (+ 1 chat)" : $" (+ {extraChatCount} chats)";
        var chatId = ChatId.Parse(chatSid);
        var link = Links.Chat(chatId);

        long capabilities = 0;
        if (mode is AudioWidgetMode.Replaying or AudioWidgetMode.Listening) {
            if (canPause)
                capabilities |= isPaused ? PlaybackStateCompat.ActionPlay : PlaybackStateCompat.ActionPause;
            capabilities |= PlaybackStateCompat.ActionStop;
        }
        var playbackStateCompat = new PlaybackStateCompat.Builder()
            .SetState(
                mode is (AudioWidgetMode.Replaying or AudioWidgetMode.Listening) && isPaused
                    ? PlaybackStateCompat.StatePaused
                    : PlaybackStateCompat.StatePlaying,
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

                var metadata = new MediaMetadataCompat.Builder()
                    .PutString(MediaMetadataCompat.MetadataKeyTitle, title)!
                    .PutString(MediaMetadataCompat.MetadataKeyArtist, text)!
                    .PutBitmap(MediaMetadataCompat.MetadataKeyAlbumArt, bitmap)!
                    .Build();
                _mediaSession.SetMetadata(metadata);
                var notification = BuildNotification(_mediaSession, link);
                StartForeground1(notification, mode);
            });

        return StartCommandResult.NotSticky;
    }

    private void OnMicCapabilityRequested(bool isMicrophoneNeeded)
    {
        // Android grants while-in-use microphone access on the serviceType of the last
        // startForeground call, not on the [Service] attribute - and a wake starts as
        // mediaPlayback only, so a press must re-issue this before the mic is opened.
        // Inline on the main thread, because the media-button dispatch runs there and the raise
        // must stay inside it; BeginInvokeOnMainThread posts even from the main thread.
        var mode = isMicrophoneNeeded ? AudioWidgetMode.Recording : AudioWidgetMode.Listening;
        if (MainThread.IsMainThread)
            Apply();
        else
            BeginDispatchToMainThread(Apply);
        return;

        void Apply() {
            if (Volatile.Read(ref _isStopping) || Volatile.Read(ref _lastMode) == (int)mode)
                return;

            var notification = Volatile.Read(ref _lastNotification) ?? BuildStartingNotification();
            StartForeground1(notification, mode);
        }
    }

    private void StartForeground1(Android.App.Notification notification, AudioWidgetMode mode)
    {
        try {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) {
                var serviceType = mode is AudioWidgetMode.Recording
                    ? ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback
                    : ForegroundService.TypeMediaPlayback;
                StartForeground(NotificationId, notification, serviceType);
            }
            else
                StartForeground(NotificationId, notification);
            Volatile.Write(ref _lastNotification, notification);
            Volatile.Write(ref _lastMode, (int)mode);
        }
        catch (Exception e) {
            // A mic FGS started over the keyguard can be rejected (SecurityException /
            // ForegroundServiceStartNotAllowedException) on some OEMs — log rather than crash.
            Log.LogError(e, "StartForeground failed (mode={Mode})", mode);
        }
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
            else {
                callback(bitmapTask.GetAwaiter().GetResult());
            }
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

    private Android.App.Notification BuildStartingNotification()
        => new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetOngoing(true)!
            .Build()!;

    private Android.App.Notification BuildNotification(MediaSessionCompat mediaSession, string link)
    {
        // PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        var viewIntent = NotificationHelper.CreateViewIntent(this, link);
        var viewPending = PendingIntent.GetActivity(this, 3, viewIntent, PendingIntentFlags.Immutable);

        var stopIntent = new Intent(this, typeof(AndroidAudioWidgetForegroundService));
        stopIntent.SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 4, stopIntent, PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentIntent(viewPending)!
            .SetOngoing(true)!
            .AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", stopPending)!;

        var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetMediaSession(mediaSession.SessionToken)!
            .SetShowActionsInCompactView(0)!;
        builder.SetStyle(mediaStyle);

        return builder.Build()!;
    }

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "Audio Widget", NotificationImportance.Low);
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
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
            => AndroidAudioWidget.Resume();

        public override void OnPause()
            => AndroidAudioWidget.Pause();

        public override void OnStop()
            => AndroidAudioWidget.Stop();
    }
}
