using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserNotificationsPanelSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserNotificationsPanelSettings>
{
    public static string KvasKey => nameof(UserNotificationsPanelSettings);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public NotificationsPanelRetention Retention { get; init; } = NotificationsPanelRetention.Hour1;
}

public enum NotificationsPanelRetention
{
    // Hour1 is 0 so a value-less blob defaults to the intended 1-hour retention, not "Immediately".
    Hour1 = 0,
    Immediately = 1,
    Minutes10 = 2,
    Hours3 = 3,
    Hours8 = 4,
    Hours24 = 5,
    // Not time-based: read chats linger until the panel is closed (the original panel behavior).
    UntilPanelClosed = 6,
}

public static class NotificationsPanelRetentionExt
{
    public static TimeSpan ToTimeSpan(this NotificationsPanelRetention retention)
        => retention switch {
            NotificationsPanelRetention.Immediately => TimeSpan.Zero,
            NotificationsPanelRetention.Minutes10 => TimeSpan.FromMinutes(10),
            NotificationsPanelRetention.Hour1 => TimeSpan.FromHours(1),
            NotificationsPanelRetention.Hours3 => TimeSpan.FromHours(3),
            NotificationsPanelRetention.Hours8 => TimeSpan.FromHours(8),
            NotificationsPanelRetention.Hours24 => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(1),
        };
}
