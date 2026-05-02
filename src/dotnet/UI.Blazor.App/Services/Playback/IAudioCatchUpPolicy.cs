namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-author audio catch-up signal source. Returns the desired catch-up
/// duration for the given author. Positive = audio playback should advance by
/// that much (drop frames or hard-skip a chunk); ≤ 0 = no correction needed.
/// </summary>
/// <remarks>
/// The signal originates from the side that owns the presentation timeline
/// (currently planned to be the video pipeline). The audio listener path
/// re-samples this every ~200 ms inside the catch-up transform; the policy
/// is expected to be cheap to call.
/// </remarks>
public interface IAudioCatchUpPolicy
{
    Task<TimeSpan> GetDesiredCatchUp(AuthorId authorId, CancellationToken cancellationToken);
}

/// <summary>
/// Default no-op policy. Always reports "no correction". A real implementation
/// will compare audio's current playing offset with the video pipeline's
/// target presentation point.
/// </summary>
public sealed class NoCatchUpPolicy : IAudioCatchUpPolicy
{
    public Task<TimeSpan> GetDesiredCatchUp(AuthorId authorId, CancellationToken cancellationToken)
        => Task.FromResult(TimeSpan.Zero);
}
