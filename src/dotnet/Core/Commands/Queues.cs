namespace ActualChat.Commands;

public static class Queues
{
    static Queues()
    {
        Chats = new QueueRef("chats");
        Users = new QueueRef("users");
    }

    public static QueueRef Chats { get; }
    public static QueueRef Users { get; }
    public static QueueRef Default => QueueRef.Default;
}
