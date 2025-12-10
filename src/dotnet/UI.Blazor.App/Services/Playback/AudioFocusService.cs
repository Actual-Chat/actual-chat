namespace ActualChat.UI.Blazor.App.Services;

public enum AudioMode { Tunes, Playback, /* HistoricalPlayback, ChatListening, */ Recording }

public delegate void RestoreFocusHandler();

public delegate RestoreFocusHandler? LostFocusCallback(bool mayRecover, bool canDuck);

public record AudioFocusConsumer(AudioMode Kind, LostFocusCallback LostFocusCallback);

public interface IAudioFocusActivation
{
    string Id { get; }
    bool IsSuspended { get; }
    void Release();
}

public class AudioFocusService : ProcessorBase
{
    public virtual Task<IAudioFocusActivation?> TryGainAudioFocus(AudioFocusConsumer consumer)
        => Task.FromResult<IAudioFocusActivation?>(FakeAudioFocusActivation.Instance);

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
