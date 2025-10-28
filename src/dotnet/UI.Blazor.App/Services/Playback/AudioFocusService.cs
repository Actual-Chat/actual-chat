namespace ActualChat.UI.Blazor.App.Services;

public enum AudioFocusConsumerKind { Tunes, Playback, /* HistoricalPlayback, ChatListening, */ Recording }

public delegate void RestoreFocusHandler();

public record AudioFocusConsumer(AudioFocusConsumerKind Kind, Func<bool, RestoreFocusHandler?> LostFocusCallback);

public interface IAudioFocusActivation
{
    string Id { get; }
    bool IsSuspended { get; }
    void Release();
}

public class AudioFocusService
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
