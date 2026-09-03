using ActualChat.Localization;
using ActualChat.Maui;
using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Microsoft.Extensions.Localization;

namespace ActualChat.App.Maui;

public sealed record ChatAttentionRequest(
    ChatId ChatId,
    long ChatPosition,
    DateTime CreatedOnUtc,
    string Title,
    string Body,
    string ImageUrl);

public sealed class ChatAttentionService
{
    private const string NotificationTag = "ChatAttentionNotification";
    private const int MaxNotificationCount = 4;
    private static readonly Lock ClassSyncObject = new ();

    public static readonly string AlarmActionPrefix = Context.PackageName + ".ChatAttention.";
    private static readonly string AlarmAction = AlarmActionPrefix + "Alarm";
    private static readonly string SnoozeAction = AlarmActionPrefix + "Snooze";
    private static readonly string NotificationGroupKey = Context.PackageName + "n.g.attention";
    private static readonly TimeSpan RemindInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SnoozeInterval = TimeSpan.FromMinutes(60);
    private static Context Context => Platform.AppContext;
    private static IStringLocalizer L => AppStrings.L;
    private static DateTime UtcNow => DateTime.UtcNow;

    private readonly Lock _syncObject = new();
    private bool _isInitialized;

    private AlarmManager AlarmManager => field ??= (AlarmManager)Context.GetSystemService(Context.AlarmService)!;

    public static ChatAttentionService Instance {
        get {
            lock (ClassSyncObject) {
                if (field == null) {
                     field = new ChatAttentionService();
                     ChatUI.OnReadPositionUpdated += arg => {
                         var (chatId, entryLid) = arg;
                         field.Clear(chatId, entryLid);
                     };
                }
                return field;
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

    public void Clear(ChatId chatId, long chatPosition)
        => DispatchOnNonMainThread(() => ClearInternal(chatId, chatPosition));

    public void Dismiss(IReadOnlyCollection<ChatId> chatIds)
        // The server already decided these chats are handled, so the positions they were asked at
        // no longer matter - and the alarm re-posts the banners until the requests are gone.
        => DispatchOnNonMainThread(() => DismissInternal(chatIds));

    public void OnHandleIntent(Intent intent)
    {
        var action = intent.Action;
        if (action == AlarmAction)
            DispatchOnNonMainThread(OnAlarmTriggered);
        else if (action == SnoozeAction)
            DispatchOnNonMainThread(OnSnooze);
    }

    private void InitInternal()
        => DoJob(null);

    private void AskInternal(ChatAttentionRequest request)
    {
        var state = GetState() ?? State.None;
        var existentRequest = state.GetRequest(request.ChatId);
        if (existentRequest != null && existentRequest.ChatPosition > request.ChatPosition)
            return;

        var requests = state.Requests;
        requests = existentRequest != null
            ? requests.Select(c => c == existentRequest ? request : c).ToArray()
            : new List<ChatAttentionRequest>(requests) { request }.ToArray();
        state = new State(UtcNow, requests);
        SetState(state);
        DoJob(state);
    }

    private void ClearInternal(ChatId chatId, long chatPosition)
    {
        var originalState = GetState();
        var state = originalState;
        if (state != null) {
            var existentRequest = state.GetRequest(chatId);
            if (existentRequest != null && existentRequest.ChatPosition <= chatPosition)
                state = new State(UtcNow, state.Requests.Where(c => c != existentRequest).ToArray());
            if (!state.HasRequest())
                state = null;
            SetState(state);
        }
        if (!ReferenceEquals(originalState, state))
            DoJob(state ?? State.None);
    }

    private void DismissInternal(IReadOnlyCollection<ChatId> chatIds)
    {
        // A whole push worth of dismissals at once: one state read and one Notify pass, instead of
        // cancelling and re-posting every surviving banner once per dismissed chat.
        var originalState = GetState();
        if (originalState is null)
            return;

        var requests = originalState.Requests.Where(c => !chatIds.Contains(c.ChatId)).ToArray();
        if (requests.Length == originalState.Requests.Length)
            return;

        var state = requests.Length > 0 ? new State(UtcNow, requests) : null;
        SetState(state);
        DoJob(state ?? State.None);
    }

    private void OnAlarmTriggered()
        => DoJob(null);

    private void DoJob(State? state)
    {
        state ??= GetState();
        Notify(state);
        ScheduleAlarm(state);
    }

    private void OnSnooze()
    {
        var state = GetState();
        if (state is null || !state.HasRequest())
            return;

        var muteThreshold = DateTime.UtcNow.Add(SnoozeInterval);
        if (!state.MuteThreshold.HasValue || state.MuteThreshold < muteThreshold) {
            state = state with { MuteThreshold = muteThreshold };
            SetState(state);
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
        var notificationManager = NotificationManagerCompat.From(Context)!;
        var activeNotifications = notificationManager.ActiveNotifications!;
        var existentNotifications = activeNotifications
            .Where(c => c.Tag == NotificationTag)
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
        // Android 16 force-silences a notification posted while another one from the same group is
        // already showing (https://issuetracker.google.com/issues/424448500). Children carry the
        // alert to GROUP_ALERT_SUMMARY and the summary is posted first, so there's never a second
        // notification competing with the one meant to make noise.
        var hasSummary = requests.Length > 1;
        for (int i = 0; i < mostImportantRequests.Length; i++) {
            var request = mostImportantRequests[i];
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
                builder.AddAction(0, L.ChatAttention_Snooze, snoozePendingIntent);

            builder.SetOnlyAlertOnce(true);
            if (hasSummary)
                builder.SetGroupAlertBehavior(NotificationCompat.GroupAlertSummary);
            var notification = builder.Build()!;
            notifications.Add((i + 1, notification));
        }

        if (hasSummary) {
            var minStartTime = requests.Min(c => c.CreatedOnUtc).ToLocalTime();
            var summaryBuilder = CreateNotification(
                minStartTime,
                L.ChatAttention_Title,
                L.ChatAttention_CheckChats_Format(requests.Select(c => c.Title).ToCommaPhrase()),
                null);
            summaryBuilder.SetGroupSummary(true);
            summaryBuilder.SetOnlyAlertOnce(true);
            var summaryNotification = summaryBuilder.Build()!;
            notifications.Insert(0, (0, summaryNotification));
        }

        foreach (var (id, notification) in notifications)
            notificationManager.Notify(NotificationTag, id, notification);
    }

    private static PendingIntent? CreateViewChatAction(string? link, ChatId? chatId)
    {
        string? sUri = null;
        if (!link.IsNullOrEmpty())
            sUri = link;
        else if (chatId is not null)
            sUri = Links.Chat(chatId);

        var intent = NotificationHelper.CreateViewIntent(Context, sUri);
        var pendingIntent = PendingIntent.GetActivity(Context, 0, intent, PendingIntentFlags.Immutable);
        return pendingIntent;
    }

    private static NotificationCompat.Builder CreateNotification(DateTime when, string tile, string content, PendingIntent? contentIntent)
    {
        var builder = new NotificationCompat.Builder(Context, NotificationHelper.Constants.AttentionChannelId)
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentTitle(tile)!
            .SetWhen((long)when.ToMoment().EpochOffset.TotalMilliseconds)!
            .SetShowWhen(true)!
            .SetContentText(content)!
            .SetOngoing(true)!
            .SetGroup(NotificationGroupKey)!
            .SetPriority((int)NotificationPriority.High)!
            .SetCategory(Android.App.Notification.CategoryReminder)!;
        // Intent that will be called for when tapping on the notification
        if (contentIntent != null)
            builder = builder.SetContentIntent(contentIntent)!;
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
        return;

        void SyncAction() {
            lock (_syncObject)
                action();
        }
    }

    private static State? GetState()
    {
        if (MauiPreferences.Get<State?>(MauiPreferences.ChatAttentionStateKey) is not { } state)
            return null;

        if (ReferenceEquals(state.Requests, null))
            state = state with { Requests = [] };
        return state;
    }

    private static void SetState(State? state)
    {
        if (state?.MuteThreshold < DateTime.UtcNow)
            state = state with { MuteThreshold = null };
        MauiPreferences.Set(MauiPreferences.ChatAttentionStateKey, state);
    }

    // Nested types

    public sealed record State(DateTime UpdatedOnUtc, ChatAttentionRequest[] Requests)
    {
        public static readonly State None = new (DateTime.MinValue, []);

        public DateTime? MuteThreshold { get; set; }

        public bool HasRequest() => Requests.Length > 0;

        public ChatAttentionRequest? GetRequest(ChatId chatId)
            => Requests.FirstOrDefault(r => r.ChatId == chatId);
    }
}
