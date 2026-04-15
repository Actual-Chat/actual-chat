using ActualLab.Diagnostics;

namespace ActualChat;

/// <summary>
/// Exception used to signal that an operation should be retried after a delay.
/// </summary>
#pragma warning disable SYSLIB0051
[Serializable]
public class PostponeException : Exception, INotAnError // Must not be ITransientException!
{
    private const string DefaultMessage = "Postponed.";

    public TimeSpan Delay { get; init; }

    public PostponeException() : base(DefaultMessage) { }
    public PostponeException(string? message) : base(message ?? DefaultMessage) { }
    public PostponeException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException) { }
}
