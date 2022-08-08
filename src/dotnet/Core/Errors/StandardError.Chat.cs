namespace ActualChat;

public static partial class StandardError
{
    public static class Chat
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static Exception Unavailable(string? message = null)
            => new UnavailableChatException(message);
    }
}

public abstract class ChatException : Exception
{
    protected ChatException() : this(null) { }
    protected ChatException(string? message) : base(message ?? "Chat-related error.") { }
    protected ChatException(string? message, Exception? inner) : base(message, inner) { }
}

public class UnavailableChatException : AccountException
{
    public UnavailableChatException() : this(null) { }
    public UnavailableChatException(string? message) : base(message ?? "This chat is unavailable or does not exist.") { }
    public UnavailableChatException(string? message, Exception? inner) : base(message, inner) { }
}
