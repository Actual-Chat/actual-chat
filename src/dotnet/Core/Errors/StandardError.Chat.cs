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

public class UnavailableChatException : KeyNotFoundException, IContentUnavailableException
{
    public UnavailableChatException() : this(null) { }
    public UnavailableChatException(string? message) : base(message ?? "The chat is unavailable or does not exist.") { }
    public UnavailableChatException(string? message, Exception? inner) : base(message, inner) { }
}
