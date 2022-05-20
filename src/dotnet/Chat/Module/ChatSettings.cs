namespace ActualChat.Chat.Module;

public class ChatSettings
{
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";

    public TimeSpan TurnOffRecordingAfterIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
