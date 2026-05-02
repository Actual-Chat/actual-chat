using ActualChat.UI.Blazor.Services;

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
/// Aligns audio playback to video presentation by reading per-author lag
/// samples published by the JS audio and video playback paths. Returns
/// max(0, audioLag - videoLag) when both signals are present; returns Zero
/// when video is paused, hidden, or the chat is audio-only — in which case
/// the audio buffer keeps playing at its natural rate.
/// </summary>
public sealed class LiveAudioCatchUpPolicy(IServiceProvider services) : IAudioCatchUpPolicy
{
    private IServiceProvider Services { get; } = services;
    private PlaybackLagTracker PlaybackLagTracker => field ??= Services.GetRequiredService<PlaybackLagTracker>();
    private ILogger Log => field ??= Services.LogFor<LiveAudioCatchUpPolicy>();

    public Task<TimeSpan> GetDesiredCatchUp(AuthorId authorId, CancellationToken cancellationToken)
    {
        var video = PlaybackLagTracker.GetVideoLag(authorId);
        if (video is null)
            return Task.FromResult(TimeSpan.Zero);

        var audio = PlaybackLagTracker.GetAudioLag(authorId);
        if (audio is null)
            return Task.FromResult(TimeSpan.Zero);

        var desired = audio.Value - video.Value;
        var desiredCatchUp = desired < Constants.Audio.AudioCatchUpDeadband
            ? TimeSpan.Zero
            : desired;
        Log.LogWarning(
            "Audio/video latency delta for {AuthorId}: "
            + "audio = {AudioLagMs:F0}ms, video = {VideoLagMs:F0}ms, delta = {DeltaMs:F0}ms, "
            + "desired catch-up = {DesiredCatchUpMs:F0}ms, deadband = {DeadbandMs:F0}ms",
            authorId,
            audio.Value.TotalMilliseconds,
            video.Value.TotalMilliseconds,
            desired.TotalMilliseconds,
            desiredCatchUp.TotalMilliseconds,
            Constants.Audio.AudioCatchUpDeadband.TotalMilliseconds);
        return Task.FromResult(desiredCatchUp);
    }
}
