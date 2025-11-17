using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media.Session;
using Android.OS;
using AndroidX.Core.App;
using Mode = ActualChat.UI.Blazor.App.Services.AudioWidgetSessionStateMode;

namespace ActualChat.App.Maui.Audio;

[Service(ForegroundServiceType = ForegroundService.TypeMediaPlayback | ForegroundService.TypeMicrophone)]
public class AudioWidgetForegroundService : Service
{
    public static class IntentExtras
    {
        public const string Mode = nameof(Mode);
        public const string ChatId = nameof(ChatId);
        public const string ChatTitle = nameof(ChatTitle);
        public const string ChatPicUri = nameof(ChatPicUri);
        public const string ExtraChatCount = nameof(ExtraChatCount);
        public const string IsPaused = nameof(IsPaused);
    }

    public const string ActionShow = "ACTION_SHOW";
    public const string ActionPause = "ACTION_PAUSE";
    public const string ActionResume = "ACTION_RESUME";
    public const string ActionStop = "ACTION_STOP";

    private const string ChannelId = "audio_widget";
    private const int NotificationId = 3001;
    private string _requestId = "";

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override void OnDestroy()
    {
        _requestId = Guid.NewGuid().ToString();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _requestId = Guid.NewGuid().ToString();
        var action = intent?.Action;

        switch (action) {
        case ActionShow:
            var mode = (Mode)intent!.Extras!.GetInt(IntentExtras.Mode);
            var chatTitle = intent.Extras!.GetString(IntentExtras.ChatTitle) ?? "Unknown chat";
            var chatSid = intent.Extras!.GetString(IntentExtras.ChatId);
            var chatPicUrl = intent.Extras!.GetString(IntentExtras.ChatPicUri) ?? "";
            var extraChatCount = intent.Extras!.GetInt(IntentExtras.ExtraChatCount);
            var isPaused = intent.Extras!.GetBoolean(IntentExtras.IsPaused);
            var text = mode switch {
                Mode.Recording => "Recording",
                Mode.RealtimePlayback => "Listening",
                Mode.HistoricalPlayback => "Historical listening",
                _ => throw new ArgumentOutOfRangeException()
            };
            var title = chatTitle;
            if (extraChatCount > 0) {
                if (extraChatCount == 1)
                    title += " (+ 1 chat)";
                else
                    title += $" (+ {extraChatCount} chats)";
            }

            var serviceType = mode is Mode.Recording
                ? ForegroundService.TypeMicrophone | ForegroundService.TypeMediaPlayback
                : ForegroundService.TypeMediaPlayback;
            var actions = mode is Mode.HistoricalPlayback
                ? GetHistoricalPlaybackActions(isPaused)
                : Actions.None;
            var chatId = ChatId.Parse(chatSid);
            var link = Links.Chat(chatId);
            Bitmap? bitmap = null;
            if (!chatPicUrl.IsNullOrEmpty()) {
                var bitmapTask = NotificationHelper.GetImageAsync(chatPicUrl);
                if (bitmapTask.IsCompleted) {
                    if (bitmapTask.IsCompletedSuccessfully) {
 #pragma warning disable VSTHRD002
                        bitmap = bitmapTask.Result;
 #pragma warning restore VSTHRD002
                    }
                }
                else {
                    var lastRequestId = _requestId;
                    _ = bitmapTask.ContinueWith(t => {
                            if (Equals(lastRequestId, _requestId)) {
                                bitmap = t.Result;
                                StartForegroundX();
                            }
                        },
                        TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            StartForegroundX();
            break;

            void StartForegroundX()
            {
                var notification = BuildNotification(title, text, bitmap, link, actions);
                StartForeground(NotificationId, notification, serviceType);
            }

        case ActionPause:
            AudioWidgetController.Pause();
            break;
        case ActionStop:
            AudioWidgetController.Stop();
            break;
        case ActionResume:
            AudioWidgetController.Resume();
            break;
        }

        return StartCommandResult.Sticky;
    }

    private static Actions GetHistoricalPlaybackActions(bool isPaused)
        => isPaused ? Actions.Resume | Actions.Stop : Actions.Pause | Actions.Stop;

    private Android.App.Notification BuildNotification(string title, string text, Bitmap? bitmap, string link, Actions actions)
    {
        var mediaSession = new MediaSession(this, "AudioWidgetForegroundService");

        var resumeIntent = new Intent(this, typeof(AudioWidgetForegroundService)).SetAction(ActionResume);
        var pauseIntent = new Intent(this, typeof(AudioWidgetForegroundService)).SetAction(ActionPause);
        var stopIntent = new Intent(this, typeof(AudioWidgetForegroundService)).SetAction(ActionStop);

        var viewIntent = NotificationHelper.CreateViewIntent(this, link);// PackageManager!.GetLaunchIntentForPackage(PackageName!)!;

        var resumePending = PendingIntent.GetService(this, 0, resumeIntent, PendingIntentFlags.Immutable);
        var pausePending = PendingIntent.GetService(this, 1, pauseIntent, PendingIntentFlags.Immutable);
        var stopPending = PendingIntent.GetService(this, 2, stopIntent, PendingIntentFlags.Immutable);
        var viewPending = PendingIntent.GetActivity(this, 3, viewIntent, PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetSmallIcon(ResourceConstant.Drawable.notification_app_icon)! //.SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)!
            .SetContentIntent(viewPending)!
            .SetOngoing(true)!
            .SetPriority((int)NotificationPriority.Low)!
            .SetVisibility((int)NotificationVisibility.Public)!;

        if (bitmap is not null)
            builder.SetLargeIcon(bitmap);

        var actionIndices = new List<int>();
        var actionIndex = 0;

        if (actions.HasFlag(Actions.Resume)) {
            builder.AddAction(Android.Resource.Drawable.IcMediaPlay, "Play", resumePending);
            actionIndices.Add(actionIndex++);
        }

        if (actions.HasFlag(Actions.Pause)) {
            builder.AddAction(Android.Resource.Drawable.IcMediaPause, "Pause", pausePending);
            actionIndices.Add(actionIndex++);
        }

        if (actions.HasFlag(Actions.Stop)) {
            builder.AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Stop", stopPending);
            actionIndices.Add(actionIndex);
        }

        var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetShowActionsInCompactView(actionIndices.ToArray())!;
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

    [Flags]
    private enum Actions
    {
        None    = 0x0,
        Resume  = 0x1,
        Pause   = 0x2,
        Stop    = 0x4,
    }
}
