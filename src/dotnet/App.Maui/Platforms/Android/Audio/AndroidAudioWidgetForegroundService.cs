using _Microsoft.Android.Resource.Designer;
using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
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
    }

    public const string ActionShow = "ACTION_SHOW";
    private const string ChannelId = "audio_widget";
    private const int NotificationId = 3001;
    private string _requestId = "";
    private MediaSessionCompat? _mediaSession;
    private ILogger Log { get; } = StaticLog.For<AndroidAudioWidgetForegroundService>();

    public override void OnCreate()
    {
        Log.LogDebug("OnCreate");
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override void OnDestroy()
    {
        Log.LogDebug("OnDestroy");
        _requestId = Guid.NewGuid().ToString();
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
        if ((intent?.Action ?? "") != ActionShow)
            return StartCommandResult.Sticky;

        var mode = (AudioWidgetMode)(intent!.Extras?.GetInt(IntentExtras.Mode) ?? 0);
        // Android requires StartForeground() within ~5s of StartForegroundService(). Call it up-front
        // with a placeholder so a throw while building the rich notification (unexpected mode,
        // ChatId.Parse) can't leave the service without one -> ForegroundServiceDidNotStartInTimeException.
        StartForeground1(BuildStartingNotification());

        var chatTitle = intent.Extras!.GetString(IntentExtras.ChatTitle) ?? "Unknown chat";
        var chatSid = intent.Extras!.GetString(IntentExtras.ChatId);
        var chatPicUrl = intent.Extras!.GetString(IntentExtras.ChatPicUri) ?? "";
        var extraChatCount = intent.Extras!.GetInt(IntentExtras.ExtraChatCount);
        var isPaused = intent.Extras!.GetBoolean(IntentExtras.IsPaused);

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
                StartForeground1(notification);
            });

        return StartCommandResult.NotSticky;

        void StartForeground1(Android.App.Notification notification)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) {
                var serviceType = mode is AudioWidgetMode.Recording
                    ? ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback
                    : ForegroundService.TypeMediaPlayback;
                StartForeground(NotificationId, notification, serviceType);
            }
            else
                StartForeground(NotificationId, notification);
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

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)!
            .SetContentIntent(viewPending)!
            .SetOngoing(true)!;

        var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetMediaSession(mediaSession.SessionToken)!;
        builder.SetStyle(mediaStyle);

        return builder.Build()!;
    }

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "Audio Widget", NotificationImportance.Low);
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }

    // Nested types
    private class Callback : MediaSessionCompat.Callback
    {
        public override void OnPlay()
            => AndroidAudioWidget.Resume();

        public override void OnPause()
            => AndroidAudioWidget.Pause();

        public override void OnStop()
            => AndroidAudioWidget.Stop();
    }
}
