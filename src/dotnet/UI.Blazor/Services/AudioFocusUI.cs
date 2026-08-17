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

    public virtual Task EnsureOutputRoute(CancellationToken cancellationToken = default)
        // Called right before audio is produced. Android hands the communication route back to its
        // earpiece default once it decides a focus holder has gone idle, which a wake always hits:
        // focus is taken seconds before the first frames arrive.
        => Task.CompletedTask;

    public virtual Task WarmUp()
        => Task.CompletedTask;

    public virtual Task YieldCommunicationMode()
        // A ring must not share the audio mode with a call: Android routes the ring stream through
        // the communication device while the mode is InCommunication, i.e. into the earpiece.
        // Callers must only yield when nothing is on the line; the pairing Restore is idempotent.
        => Task.CompletedTask;

    public virtual Task RestoreCommunicationMode()
        => Task.CompletedTask;

    public virtual AudioFocusDiagnostics GetDiagnostics()
        => AudioFocusDiagnostics.Unsupported;

    // Nested types

    private class FakeScope : AudioFocusScope
    {
        public static readonly FakeScope Instance = new();

        public override void Dispose()
        { }
    }
}

/// <summary>
/// Read-only snapshot of the platform audio-focus / session state for the Audio
/// Diagnostics UI. <see cref="IsSupported"/> is false where focus is implicit (web).
/// </summary>
public sealed record AudioFocusDiagnostics(
    bool IsSupported,
    AudioFocusMode ActiveMode,
    bool IsInterrupted,
    bool IsSuspended,
    bool IsSessionConfigured,
    IReadOnlyList<AudioFocusScopeInfo> Scopes,
    AppleAudioSessionDiagnostics? Session)
{
    public static readonly AudioFocusDiagnostics Unsupported =
        new(false, AudioFocusMode.Tune, false, false, false, [], null);

    public bool Equals(AudioFocusDiagnostics? other)
        => other is not null
            && IsSupported == other.IsSupported
            && ActiveMode == other.ActiveMode
            && IsInterrupted == other.IsInterrupted
            && IsSuspended == other.IsSuspended
            && IsSessionConfigured == other.IsSessionConfigured
            && Equals(Session, other.Session)
            && Scopes.SequenceEqual(other.Scopes);

    public override int GetHashCode()
        => HashCode.Combine(IsSupported, ActiveMode, IsInterrupted, IsSuspended, IsSessionConfigured, Scopes.Count, Session);
}

/// <summary>
/// One audio-focus mode and the number of active scopes currently holding it.
/// </summary>
public sealed record AudioFocusScopeInfo(AudioFocusMode Mode, int Count);

/// <summary>
/// Native AVAudioSession snapshot (iOS / Mac Catalyst). Strings avoid an
/// AVFoundation dependency in UI.Blazor.
/// </summary>
public sealed record AppleAudioSessionDiagnostics(
    string Category,
    string Mode,
    bool IsOtherAudioPlaying,
    IReadOnlyList<string> OutputRoutes,
    double SampleRate,
    double IOBufferDuration,
    int InputChannelCount)
{
    public bool Equals(AppleAudioSessionDiagnostics? other)
        => other is not null
            && Category == other.Category
            && Mode == other.Mode
            && IsOtherAudioPlaying == other.IsOtherAudioPlaying
            && SampleRate.Equals(other.SampleRate)
            && IOBufferDuration.Equals(other.IOBufferDuration)
            && InputChannelCount == other.InputChannelCount
            && OutputRoutes.SequenceEqual(other.OutputRoutes);

    public override int GetHashCode()
        => HashCode.Combine(Category, Mode, IsOtherAudioPlaying, OutputRoutes.Count,
            SampleRate, IOBufferDuration, InputChannelCount);
}
