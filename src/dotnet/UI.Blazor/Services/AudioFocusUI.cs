namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Defines the type of audio activity for focus management. Ordered by precedence:
/// when several requesters are active, the highest mode wins. Listening is a transient
/// per-utterance grab for incoming realtime speech, so other apps' audio resumes in the
/// gaps; Playback is a user-initiated replay that holds focus for its whole duration.
/// </summary>
public enum AudioFocusMode { Tune, Listening, Playback, Recording }

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
    public virtual AudioFocusMode ActiveMode => AudioFocusMode.Tune;
    public virtual bool IsSuspended
        // True while the held focus is lost to another app and awaiting its recover callback.
        // Requesting focus in that state is harmful: a denied renew wipes the pending restores.
        => false;
    public virtual bool IsCommunicationFocus
        // Whether the focus we hold actually took the communication route. ActiveMode no longer
        // implies it: a recording under car projection asks for Media usage and stays in Normal.
        => false;

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

    public virtual Task EnsureBuiltinSpeakerRoute(CancellationToken cancellationToken = default)
        // Playback pinned to the phone speaker (car audio settings) needs an explicit route: unlike
        // EnsureOutputRoute, this must never fall back to a Bluetooth device.
        => Task.CompletedTask;

    public virtual Task SetCallActive(bool isCallActive, bool hasVideo)
        => Task.CompletedTask;

    // The platform's own view - what's connected, where the sound is - and null where it offers
    // nothing to choose from. Which output a call should be on is CallUI's business.
    public virtual IState<AudioOutputRoutes>? OutputRoutes => null;

    // Null puts the call back on the platform's defaults.
    public virtual Task ApplyOutputRoute(string? routeId)
        => Task.CompletedTask;

    public virtual AudioFocusDiagnostics GetDiagnostics()
        => AudioFocusDiagnostics.Unsupported;

    public virtual AudioOutputKind? GetCurrentOutputKind()
        => null;

    // Nested types

    private sealed class FakeScope : AudioFocusScope
    {
        public static readonly FakeScope Instance = new();

        public override void Dispose()
        { }
    }
}

public enum AudioOutputKind
{
    Phone,
    Speaker,
    Headphones,
    Bluetooth,
    Car,
    Other,
}

/// <summary>
/// A device call audio can play through. <see cref="Name"/> is the device's own name,
/// empty where the kind alone names it.
/// </summary>
public sealed record AudioOutputRoute(string Id, AudioOutputKind Kind, string Name = "")
{
    // The built-in pair is always reachable, so it's addressable without a listing to look it up in.
    public const string PhoneId = "phone";
    public const string SpeakerId = "speaker";

    public static string GetDefaultBuiltinId(bool hasVideo)
        // A voice call starts at the ear, like a phone call; a video call on the speaker, where the
        // phone is held out to be seen. A connected device outranks either, whatever the platform.
        => hasVideo ? SpeakerId : PhoneId;

    public bool IsExternal => Kind is not (AudioOutputKind.Phone or AudioOutputKind.Speaker);
}

/// <summary>
/// The outputs call audio can be switched between right now, and the one it plays through.
/// </summary>
public sealed record AudioOutputRoutes(IReadOnlyList<AudioOutputRoute> Routes, string CurrentId)
{
    public static readonly AudioOutputRoutes None = new([], "");

    public AudioOutputRoute? Current => Routes.FirstOrDefault(x => x.Id == CurrentId);
    public bool HasExternal => Routes.Any(x => x.IsExternal);

    public bool Equals(AudioOutputRoutes? other)
        => other is not null
            && CurrentId == other.CurrentId
            && Routes.SequenceEqual(other.Routes);

    public override int GetHashCode()
        => HashCode.Combine(CurrentId, Routes.Count);
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
        => HashCode.Combine(
            IsSupported,
            ActiveMode,
            IsInterrupted,
            IsSuspended,
            IsSessionConfigured,
            Scopes.Count,
            Session);
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
    IReadOnlyList<string> InputRoutes,
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
            && OutputRoutes.SequenceEqual(other.OutputRoutes)
            && InputRoutes.SequenceEqual(other.InputRoutes);

    public override int GetHashCode()
        => HashCode.Combine(Category, Mode, IsOtherAudioPlaying, OutputRoutes.Count,
            InputRoutes.Count, SampleRate, IOBufferDuration, InputChannelCount);
}
