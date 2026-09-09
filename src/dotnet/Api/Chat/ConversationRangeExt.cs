namespace ActualChat.Chat;

public static class ConversationRangeExt
{
    extension(Range<long> range)
    {
        public bool IsOpenEnded => range.End == long.MaxValue;
    }
}
