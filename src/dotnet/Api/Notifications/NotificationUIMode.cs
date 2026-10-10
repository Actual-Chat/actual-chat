namespace ActualChat.Notifications;

public enum NotificationUIMode
{
    Auto = 0,
    Chats = 1,
    Notifications = 2,
}

public static class NotificationUIModeExt
{
    public static string ToQueryValue(this NotificationUIMode mode)
        => mode switch {
            NotificationUIMode.Chats => "0",
            NotificationUIMode.Notifications => "1",
            _ => "auto",
        };

    public static NotificationUIMode Parse(string? value)
        => value switch {
            "0" => NotificationUIMode.Chats,
            "1" => NotificationUIMode.Notifications,
            _ => NotificationUIMode.Auto,
        };
}
