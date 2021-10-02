using ActualChat.Mathematics;

namespace ActualChat.Chat;

public static class ChatConstants
{
    public static string DefaultChatId { get; } = "the-actual-one";
    public static LogCover<long, long> IdLogCover { get; } = LogCover.Default.Long;
    public static LogCover<Moment, TimeSpan> TimeLogCover { get; } = LogCover.Default.Moment;
}
