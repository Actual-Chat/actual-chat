using ActualChat.Mathematics;
using ActualChat.Mathematics.Internal;

namespace ActualChat.Chat
{
    public static class ChatConstants
    {
        public static string DefaultChatId { get; } = "the-actual-one";
        public static LogCover<long, long> IdLogCover { get; } = LogCover.Default.Long;
    }
}
