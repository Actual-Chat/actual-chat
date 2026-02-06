namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Defines the type of audio activity for focus management.
/// </summary>
public enum AudioMode { Tunes, Playback, Recording }

public delegate void RestoreFocusHandler();

public delegate RestoreFocusHandler? LostFocusCallback(bool mayRecover, bool canDuck);

/// <summary>
/// Represents an audio consumer requesting focus with a callback for focus loss events.
/// </summary>
public record AudioFocusConsumer(AudioMode Kind, LostFocusCallback LostFocusCallback);

/// <summary>
/// Represents an active audio focus grant that can be released or suspended.
/// </summary>
public interface IAudioFocusActivation
{
    string Id { get; }
    bool IsSuspended { get; }
    void Release();
}

/// <summary>
/// Manages audio focus for playback and recording, handling focus conflicts between consumers.
/// </summary>
public class AudioFocusService : ProcessorBase
{
    public virtual Task<IAudioFocusActivation?> TryGainAudioFocus(AudioFocusConsumer consumer)
        => Task.FromResult<IAudioFocusActivation?>(FakeAudioFocusActivation.Instance);

    public virtual Task Recover(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // Nested types
    private class FakeAudioFocusActivation : IAudioFocusActivation
    {
        public static readonly FakeAudioFocusActivation Instance = new ();

        public string Id => "FAKE";
        public bool IsSuspended => false;

        public void Release()
        {
        }
    }
}
