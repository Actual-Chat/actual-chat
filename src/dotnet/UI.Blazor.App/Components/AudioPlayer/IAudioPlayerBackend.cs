namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioPlayerBackend
{
    // isStarving is web-only: the feeder worklet is what distinguishes running dry from merely
    // being below the jitter target, so the native engines leave it at the default.
    void OnPlaying(double offset, bool isPaused, bool isBufferLow, bool isStarving = false);
    void OnPresentationLag(TimeSpan lag);
    void OnEnded(string? errorMessage);
}
