namespace ActualChat;

public static partial class StandardError
{
    public static class UserAuthor
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static Exception Unavailable(string? message = null)
            => new UnavailableUserAuthorException(message);
    }
}

public class UnavailableUserAuthorException : KeyNotFoundException, IContentUnavailableException
{
    public UnavailableUserAuthorException() : this(null) { }
    public UnavailableUserAuthorException(string? message) : base(message ?? "The user unavailable or does not exist.") { }
    public UnavailableUserAuthorException(string? message, Exception? inner) : base(message, inner) { }
}
