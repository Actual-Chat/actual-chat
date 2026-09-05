namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Wraps a failed action's exception with a localized <see cref="Exception.Message"/>,
/// preserving the original as <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class LocalizedError : Exception
{
    private const string DefaultMessage = "Action failed.";

    public LocalizedError() : base(DefaultMessage) { }
    public LocalizedError(string? message) : base(message ?? DefaultMessage) { }
    public LocalizedError(string? message, Exception? innerException)
        : base(message ?? DefaultMessage, innerException) { }
}
