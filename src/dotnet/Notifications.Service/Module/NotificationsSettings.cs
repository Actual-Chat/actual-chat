namespace ActualChat.Notifications.Module;

public sealed class NotificationsSettings
{
    public bool EnableWalkieTalkiePush { get; set; } = true;
    public TimeSpan WalkieTalkieWakeTtl { get; set; } = TimeSpan.FromSeconds(30);
    public int WalkieTalkieMaxChatMembers { get; set; } = 100;
}
