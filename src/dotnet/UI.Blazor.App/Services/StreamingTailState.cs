namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// One pass over a chat's tail, answering both live-transcript questions: the lowest lid still
/// speaking (which the live block must not fold away) and whether the recording placeholder should
/// stand down because a real transcript is speaking for it.
/// </summary>
public sealed record StreamingTailState(long FloorLid, bool IsSuppressed, Moment? ExpiresAt)
{
    // Null ExpiresAt with IsSuppressed means only a change can lift it - there is nothing to time out.
    public static readonly StreamingTailState None = new(long.MaxValue, false, null);
}
