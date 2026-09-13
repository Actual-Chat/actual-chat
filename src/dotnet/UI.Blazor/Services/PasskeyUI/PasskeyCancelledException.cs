namespace ActualChat.UI.Blazor.Services;

public sealed class PasskeyCancelledException : Exception
{
    private const string DefaultMessage = "The passkey prompt was dismissed.";

    public PasskeyCancelledException() : base(DefaultMessage) { }
    public PasskeyCancelledException(string? message) : base(message ?? DefaultMessage) { }
    public PasskeyCancelledException(string? message, Exception? innerException)
        : base(message ?? DefaultMessage, innerException) { }
}
