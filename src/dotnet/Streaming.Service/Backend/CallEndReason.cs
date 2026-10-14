namespace ActualChat.Streaming;

/// <summary>
/// Which request or check closed a call. The <see cref="Chat.CallOutcome"/> is the call's story for its
/// parties; this is the server's, and a call with the same outcome can end for several of these.
/// </summary>
public enum CallEndReason
{
    Canceled,
    HungUp,
    PartyLeft,
    Declined,
    NoAnswer,
    Busy,
    ConnectTimeout,
    Lost,
    SessionClosed,
}
