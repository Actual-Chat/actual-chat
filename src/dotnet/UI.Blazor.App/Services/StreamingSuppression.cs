namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Whether the recording placeholder should stand down because a real transcript is speaking for it,
/// and when that answer lapses on its own.
/// </summary>
public sealed record StreamingSuppression(bool IsSuppressed, Moment? ExpiresAt)
{
    // Null ExpiresAt with IsSuppressed means only a change can lift it - there is nothing to time out.
    public static readonly StreamingSuppression None = new(false, null);
    public static readonly StreamingSuppression Indefinite = new(true, null);
}
