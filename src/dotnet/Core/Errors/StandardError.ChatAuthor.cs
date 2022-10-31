namespace ActualChat;

public static partial class StandardError
{
    public static class Author
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static Exception Unavailable(string? message = null)
            => new UnavailableAuthorException(message);
    }
}

public class UnavailableAuthorException : KeyNotFoundException, IContentUnavailableException
{
    public UnavailableAuthorException() : this(null) { }
    public UnavailableAuthorException(string? message) : base(message ?? "You are not a member of this chat.") { }
    public UnavailableAuthorException(string? message, Exception? inner) : base(message, inner) { }
}
