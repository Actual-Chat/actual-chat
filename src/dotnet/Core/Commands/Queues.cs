namespace ActualChat.Commands;

public static class Queues
{
    public static QueueRef Default { get; set; } = new("default");
    public static QueueRef Chats { get; } = new("chats");
    public static QueueRef Users { get; } = new("users");
}
