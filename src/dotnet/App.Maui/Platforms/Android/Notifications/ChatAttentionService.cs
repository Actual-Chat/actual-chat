using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using AndroidX.Core.App;

namespace ActualChat.App.Maui;

public record ChatAttentionRequest(
    Symbol ChatId,
    long ChatPosition,
    DateTime CreatedOnUtc,
    string Title,
    string Body,
    string ImageUrl);

public class ChatAttentionService
{
    private const string PreferencesKey = "ChatAttention";
    private const string NotificationTag = "ChatAttentionNotification";
    private const int MaxNotificationCount = 4;

    private static readonly object ClassSyncObject = new ();
    private static ChatAttentionService? _instance;
    public static readonly string AlarmActionPrefix = Context.PackageName + ".ChatAttention.";
    private static readonly string AlarmAction = AlarmActionPrefix + "Alarm";
    private static readonly string SnoozeAction = AlarmActionPrefix + "Snooze";
    private static readonly string NotificationGroupKey = Context.PackageName + "n.g.attention";
    private static readonly TimeSpan RemindInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SnoozeInterval = TimeSpan.FromMinutes(60);

    private readonly object _syncObject = new();
    private AlarmManager? _alarmManager;
    private Option<State?> _cachedState = Option<State?>.None;
    private bool _isInitialized;

    private static Context Context => Platform.AppContext;
    private static DateTime UtcNow => DateTime.UtcNow;

    private AlarmManager AlarmManager => _alarmManager ??= (AlarmManager)Context.GetSystemService(Context.AlarmService)!;

    public static ChatAttentionService Instance {
        get {
            lock (ClassSyncObject) {
                if (_instance == null) {
                     _instance = new ChatAttentionService();
                     ChatUI.OnReadPositionUpdated += tuple => {
                         var (chatId, entryLid) = tuple;
                         _instance.Clear(chatId.Value, entryLid);
                     };
                }
                return _instance;
            }
        }
    }

    private ChatAttentionService() { }

    public void Init()
    {
        if (_isInitialized)
            return;

        _ = Task.Delay(TimeSpan.FromSeconds(30))
            .ContinueWith(_ => InitInternal(), TaskScheduler.Default);
        _isInitialized = true;
    }

    public void Ask(ChatAttentionRequest request)
        => DispatchOnNonMainThread(() => AskInternal(request));

    public void Clear(Symbol chatId, long chatPosition)
        => DispatchOnNonMainThread(() => ClearInternal(chatId, chatPosition));

    public void OnHandleIntent(Intent intent)
    {
        var action = intent.Action;
        if (OrdinalEquals(action, AlarmAction))
            DispatchOnNonMainThread(OnAlarmTriggered);
        else if (OrdinalEquals(action, SnoozeAction))
            DispatchOnNonMainThread(OnSnooze);
    }

    private void InitInternal()
        => DoJob(null);

    private void AskInternal(ChatAttentionRequest request)
    {
        var state = GetPersistedState();
        state ??= State.None;
        var existentRequest = state.GetRequest(request.ChatId);
        if (existentRequest != null && existentRequest.ChatPosition > request.ChatPosition)
            return;

        var requests = state.Requests;
        requests = existentRequest != null
            ? requests.Select(c => c == existentRequest ? request : c).ToArray()
            : new List<ChatAttentionRequest>(requests) { request }.ToArray();
        state = new State(UtcNow, requests);
        PersistState(state);
        DoJob(state);
    }

    private void ClearInternal(Symbol chatId, long chatPosition)
    {
        var originalState = GetPersistedState();
        var state = originalState;
        if (state != null) {
            var existentRequest = state.GetRequest(chatId);
            if (existentRequest != null && existentRequest.ChatPosition <= chatPosition)
                state = new State(UtcNow, state.Requests.Where(c => c != existentRequest).ToArray());
            if (!state.HasRequest())
                state = null;
            PersistState(state);
        }
        if (!ReferenceEquals(originalState, state))
            DoJob(state ?? State.None);
    }

    private void OnAlarmTriggered()
        => DoJob(null);

    private void DoJob(State? state)
    {
        state ??= GetPersistedState();
        Notify(state);
        ScheduleAlarm(state);
    }

    private void OnSnooze()
    {
        var state = GetPersistedState();
        if (state is null || !state.HasRequest())
            return;

        var muteThreshold = DateTime.UtcNow.Add(SnoozeInterval);
        if (!state.MuteThreshold.HasValue || state.MuteThreshold < muteThreshold) {
            state = state with { MuteThreshold = muteThreshold };
            PersistState(state);
        }

        Notify(state, false, false);
        ScheduleAlarm(state);
    }

    private void ScheduleAlarm(State? state)
        => ScheduleAlarm(state?.HasRequest() ?? false, state?.MuteThreshold, RemindInterval);

    private void ScheduleAlarm(bool schedule, DateTime? muteThreshold, TimeSpan dueTime)
    {
        var intent = new Intent(Context, typeof(AlarmReceiver));
        intent.SetAction(AlarmAction);
        var pendingIntent = PendingIntent.GetBroadcast(Context,
            0,
            intent,
            PendingIntentFlags.Mutable | PendingIntentFlags.CancelCurrent)!;
        if (schedule) {
            if (muteThreshold.HasValue) {
                var now = DateTime.UtcNow;
                if (muteThreshold.Value > now.Add(dueTime))
                    dueTime = muteThreshold.Value - now;
            }
            var nextMoment = Java.Lang.JavaSystem.CurrentTimeMillis() + (long)dueTime.TotalMilliseconds;
            AlarmManager.SetWindow(AlarmType.RtcWakeup, nextMoment, 10 * 60_000, pendingIntent);
        }
        else
            AlarmManager.Cancel(pendingIntent);
    }

    private static void Notify(State? state, bool addSnooze = true, bool clear = true)
    {
        var notificationManager = NotificationManagerCompat.From(Context);
        var activeNotifications = notificationManager.ActiveNotifications;
        var existentNotifications = activeNotifications
            .Where(c => OrdinalEquals(c.Tag, NotificationTag))
            .ToArray();
        var hasRequests = state != null && state.HasRequest();
        if (clear || !hasRequests) {
            foreach (var existentNotification in existentNotifications)
                notificationManager.Cancel(NotificationTag, existentNotification.Id);
        }
        if (!hasRequests)
            return;

        NotificationHelper.EnsureAttentionNotificationChannelExist(Context, NotificationHelper.Constants.AttentionChannelId);

        var snoozeIntent = new Intent(Context, typeof(AlarmReceiver));
        snoozeIntent.SetAction(SnoozeAction);
        var snoozePendingIntent = PendingIntent.GetBroadcast(Context,
            0,
            snoozeIntent,
            PendingIntentFlags.Immutable);

        var notifications = new List<(int, Android.App.Notification)>();
        var requests = state!.Requests;

        var mostImportantRequests = requests
            .OrderBy(c => c.CreatedOnUtc)
            .Take(MaxNotificationCount)
            .ToArray();
        for (int i = 0; i < mostImportantRequests.Length; i++) {
            var request = requests[i];
            var title = request.Title;
            var content = request.Body;

            var viewChatActionIntent = CreateViewChatAction(null, request.ChatId);
            var builder = CreateNotification(
                request.CreatedOnUtc,
                title,
                content,
                viewChatActionIntent);

            var imageUrl = request.ImageUrl;
            if (!imageUrl.IsNullOrEmpty()) {
                var largeImage = NotificationHelper.GetImage(imageUrl);
                if (largeImage != null)
                    builder.SetLargeIcon(largeImage);
            }

            if (addSnooze)
                builder.AddAction(0, "Snooze", snoozePendingIntent);

            builder.SetOnlyAlertOnce(true);
            var notification = builder.Build();
            notifications.Add((i + 1, notification));
        }

        if (requests.Length > 1) {
            var minStartTime = requests.Min(c => c.CreatedOnUtc).ToLocalTime();
            var summaryBuilder = CreateNotification(
                minStartTime,
                "Chat attention required",
                "Please check chats: " + requests.Select(c => c.Title).ToCommaPhrase(),
                null);
            summaryBuilder.SetGroupSummary(true);
            summaryBuilder.SetOnlyAlertOnce(true);
            var summaryNotification = summaryBuilder.Build();
            notifications.Add((0, summaryNotification));
        }

        foreach (var (id, notification) in notifications)
            notificationManager.Notify(NotificationTag, id, notification);
    }

    private static PendingIntent? CreateViewChatAction(string? link, string? sChatId)
    {
        if (!ChatId.TryParse(sChatId, out var chatId))
            chatId = ChatId.None;

        string? sUri = null;
        if (!link.IsNullOrEmpty())
            sUri = link;
        else if (!chatId.IsNone)
            sUri = Links.Chat(chatId);

        var intent = NotificationHelper.CreateViewIntent(Context, sUri);
        var pendingIntent = PendingIntent.GetActivity(Context, 0, intent, PendingIntentFlags.Immutable);
        return pendingIntent;
    }

    private static NotificationCompat.Builder CreateNotification(DateTime when, string tile, string content, PendingIntent? contentIntent)
    {
        var builder = new NotificationCompat.Builder(Context, NotificationHelper.Constants.AttentionChannelId)
            .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)
            .SetColor(0x0036A3)
            .SetContentTitle(tile)
            .SetWhen((long)when.ToMoment().EpochOffset.TotalMilliseconds)
            .SetShowWhen(true)
            .SetContentText(content)
            .SetOngoing(true)
            .SetGroup(NotificationGroupKey)
            .SetPriority((int)NotificationPriority.High)
            .SetCategory(Android.App.Notification.CategoryReminder);
        // Intent that will be called for when tapping on the notification
        if (contentIntent != null)
            builder.SetContentIntent(contentIntent);
        return builder;
    }

    private void DispatchOnNonMainThread(Action action)
    {
        // Notify method use NotificationHelper.GetImage may invoke AndroidUtils.WaitForAndApplyImageDownload
        // which uses blocking API https://developers.google.com/android/reference/com/google/android/gms/tasks/Tasks#await(com.google.android.gms.tasks.Task%3CTResult%3E)
        // that is not allowed for using on main application thread.
        // So we offload all work to non-main thread.
        if (AndroidUtils.IsMainThread())
            _ = Task.Run(SyncAction);
        else
            SyncAction();

        void SyncAction() {
            lock (_syncObject)
                action();
        }
    }

    private State? GetPersistedState()
    {
        if (_cachedState.HasValue)
            return _cachedState.Value;

        var json = Preferences.Default.Get(PreferencesKey, "");
        if (json.IsNullOrEmpty())
            return null;

        try {
 #pragma warning disable IL2026
            return JsonSerializer.Deserialize<State>(json);
 #pragma warning restore IL2026
        }
        catch {
            return null;
        }
    }

    private void PersistState(State? state)
    {
        if (state == null)
            Preferences.Default.Remove(PreferencesKey);
        else {
            state = SimplifyState();
 #pragma warning disable IL2026
            var json = JsonSerializer.Serialize(state);
 #pragma warning restore IL2026
            Preferences.Default.Set(PreferencesKey, json);
        }
        _cachedState = new Option<State?>(true, state);

        State SimplifyState()
        {
            if (state.MuteThreshold.HasValue && state.MuteThreshold < DateTime.UtcNow)
                state = state with { MuteThreshold = null };
            return state;
        }
    }

    public record State(DateTime UpdatedOnUtc, ChatAttentionRequest[] Requests)
    {
        public static readonly State None = new (DateTime.MinValue, []);

        public DateTime? MuteThreshold { get; set; }

        public bool HasRequest() => Requests.Length > 0;

        public ChatAttentionRequest? GetRequest(Symbol chatId)
            => Requests.FirstOrDefault(r => r.ChatId.Equals(chatId));
    }
}
