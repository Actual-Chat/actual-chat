namespace ActualChat;

public static partial class StandardError
{
    public static class Chat
    {
        public static Exception NonTemplate(string? message = null)
            => new NonTemplateException(message);
    }
}

public abstract class ChatException : Exception
{
    protected ChatException() : this(null) { }
    protected ChatException(string? message) : base(message ?? "Chat-related error.") { }
    protected ChatException(string? message, Exception? inner) : base(message, inner) { }
}

public class NonTemplateException : AccountException
{
    public NonTemplateException() : this(null) { }
    public NonTemplateException(string? message) : base(message ?? "Public chat template is expected.") { }
    public NonTemplateException(string? message, Exception? inner) : base(message, inner) { }
}
