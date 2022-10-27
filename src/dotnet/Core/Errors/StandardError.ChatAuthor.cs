namespace ActualChat;

public static partial class StandardError
{
    public static class ChatAuthor
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static Exception Unavailable(string? message = null)
            => new UnavailableChatAuthorException(message);
    }
}

public class UnavailableChatAuthorException : KeyNotFoundException, IContentUnavailableException
{
    public UnavailableChatAuthorException() : this(null) { }
    public UnavailableChatAuthorException(string? message) : base(message ?? "You are not a member of this chat.") { }
    public UnavailableChatAuthorException(string? message, Exception? inner) : base(message, inner) { }
}
