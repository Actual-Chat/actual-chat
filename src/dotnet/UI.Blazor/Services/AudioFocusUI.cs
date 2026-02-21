namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Defines the type of audio activity for focus management.
/// </summary>
public enum AudioFocusMode { Tune, Playback, Recording }

public delegate void AudioFocusRestoreHandler();

public delegate AudioFocusRestoreHandler? AudioFocusLostHandler(bool mayRecover, bool canDuck);

/// <summary>
/// Represents an audio consumer requesting focus with a callback for focus loss events.
/// </summary>
public record struct AudioFocusRequester(AudioFocusMode Kind, AudioFocusLostHandler AudioFocusLostHandler);

/// <summary>
/// Represents an active audio focus grant that can be released or suspended.
/// </summary>
public abstract class AudioFocusScope : IDisposable
{
    private static long _lastIndex;

    private long Index { get; } = Interlocked.Increment(ref _lastIndex);

    public bool IsSuspended { get; private set; }

    public abstract void Dispose();

    public void Suspend(bool isSuspended)
        => IsSuspended = isSuspended;

    public override string ToString()
        => $"{GetType().GetName()}(#{Index})";
}

/// <summary>
/// Manages audio focus for playback and recording, handling focus conflicts between consumers.
/// </summary>
public class AudioFocusUI : ProcessorBase
{
    public virtual Task<AudioFocusScope?> TryAcquire(AudioFocusRequester requester)
        => Task.FromResult<AudioFocusScope?>(FakeScope.Instance);

    public virtual Task TryRecover(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task WarmUp()
        => Task.CompletedTask;

    // Nested types

    private class FakeScope : AudioFocusScope
    {
        public static readonly FakeScope Instance = new();

        public override void Dispose()
        { }
    }
}
