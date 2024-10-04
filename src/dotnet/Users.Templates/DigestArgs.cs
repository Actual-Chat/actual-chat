namespace ActualChat.Users.Templates;

public record DigestParameters
{
    public required IReadOnlyCollection<DigestChat> UnreadChats { get; init; }
    public required int OtherUnreadCount { get; init; }
    public required string OtherUnreadLink { get; init; }

    public record DigestChat
    {
        public required string Name { get; init; }
        public required string Link { get; init; }
        public required long UnreadCount { get; init; }
        public required IReadOnlyCollection<string> BulletPoints { get; init; }
    }
}
