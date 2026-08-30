using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AudioSession(AppUIHub hub) : IAsyncDisposable
{
    // Past the hot window: while that window is open the owner legitimately stays PTT-held with
    // no callback in between, so anything shorter would revert a live PTT session.
    private static readonly TimeSpan OwnerWatchdogTimeout =
        Constants.Audio.PttIdleTimeout + TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OwnerWatchdogPeriod = TimeSpan.FromSeconds(30);
    // A heartbeat, not a hold - so unlike a leaked latch it can't wedge the watchdog.
    private static readonly TimeSpan PlaybackActivityTimeout = TimeSpan.FromSeconds(10);

    private static readonly Lock OwnerLock = new();
    private static int _owner;
    private static long _ownerChangedAt;
    private static int _isOwnerWatchdogRunning;
    private static Action? _ownerWatchdogRecovery;
    private static long _playbackActivityAt;

    private AppUIHub Hub { get; } = hub;
    private static ILogger OwnerLog => field ??= StaticLog.For(typeof(AudioSession));
    private ILogger Log => field ??= Hub.LogFor(GetType());
    public static AudioSessionOwner Owner => (AudioSessionOwner)Volatile.Read(ref _owner);
    public static bool MayActivateNow => AudioSessionOwnership.MayActivate(Owner);

    public static void SetOwner(AudioSessionOwner owner)
        => PublishOwner(owner);

    public static void ReleaseOwner(AudioSessionRelease release, bool hasLivePlayback = false)
    {
        lock (OwnerLock)
            PublishOwnerUnsafe(AudioSessionOwnership.OnReleased(Owner, release, hasLivePlayback));

        ArmOwnerWatchdog();
    }

    public static void SetOwnerWatchdogRecovery(Action recovery)
        => Volatile.Write(ref _ownerWatchdogRecovery, recovery);

    public static void NotifyPlaybackActivity()
        => Volatile.Write(ref _playbackActivityAt, CpuTimestamp.Now.Value);

    public ValueTask DisposeAsync()
        => BackgroundTask.Run(() => DispatchToMainThread(() => {
                    if (!AudioSessionOwnership.MayActivate(Owner))
                        return;

                    var session = AVAudioSession.SharedInstance();
                    session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation)
                        .Assert("Failed to deactivate session");
                }),
                Log,
                "Failed to dispose AudioSession")
            .ToValueTask();

    public Task<AudioSessionSetup> Reconfigure(AudioFocusMode mode)
        => DispatchToMainThread(() => ReconfigureUnsafe(mode));

    public Task Deactivate()
        => DispatchToMainThread(DeactivateUnsafe);

    public Task<AudioSessionSetup> Reactivate(AudioFocusMode mode)
        => DispatchToMainThread(() => ReactivateUnsafe(mode));

    public Task ApplyOutputRoute(AudioFocusMode mode, bool mustDropOverride = false)
        => DispatchToMainThread(() => ApplyOutputRouteUnsafe(mode, mustDropOverride));

    public AppleAudioSessionDiagnostics? GetDiagnostics()
    {
        try {
            var session = AVAudioSession.SharedInstance();
            var route = session.CurrentRoute;
            var outputRoutes = route.Outputs.Select(Describe).ToList();
            var inputRoutes = route.Inputs.Select(Describe).ToList();
            // SampleRate/IOBufferDuration are what the OS actually granted, not what we asked for -
            // VoiceProcessingIO commonly overrides both, and the granted rate drives AEC cost.
            return new AppleAudioSessionDiagnostics(session.Category,
                session.Mode,
                session.OtherAudioPlaying,
                outputRoutes,
                inputRoutes,
                session.SampleRate,
                session.IOBufferDuration,
                (int)session.InputNumberOfChannels);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to get audio session diagnostics");
            return null;
        }

        static string Describe(AVAudioSessionPortDescription port)
            => $"{port.PortName} ({port.PortType})";
    }

    // Private methods

    private static void PublishOwner(AudioSessionOwner owner)
    {
        lock (OwnerLock)
            PublishOwnerUnsafe(owner);

        ArmOwnerWatchdog();
    }

    private static void PublishOwnerUnsafe(AudioSessionOwner owner)
    {
        // Owner and its timestamp are one decision, and the watchdog re-reads both under the same
        // lock before reverting - otherwise a callback landing mid-decision is silently clobbered.
        Volatile.Write(ref _ownerChangedAt, CpuTimestamp.Now.Value);
        Volatile.Write(ref _owner, (int)owner);
    }

    private static void ArmOwnerWatchdog()
    {
        if (Owner == AudioSessionOwner.App)
            return;
        if (Interlocked.CompareExchange(ref _isOwnerWatchdogRunning, 1, 0) != 0)
            return;

        _ = BackgroundTask.Run(
            WatchOwner, OwnerLog, "The audio session owner watchdog failed", CancellationToken.None);
    }

    private static async Task WatchOwner()
    {
        try {
            while (true) {
                await Task.Delay(OwnerWatchdogPeriod).ConfigureAwait(false);
                if (Owner == AudioSessionOwner.App)
                    return;
                if (!IsOwnerStuck())
                    continue;

                if (TryRevertOwner() is not { } stuckOwner)
                    continue;

                // MayActivate is false for both PTT owners, so a stuck one leaves the whole app
                // unable to activate its own session - every tune, playback and recording dies.
                OwnerLog.LogWarning(
                    "The audio session was owned by {Owner} with no PTT callback - reverted to App", stuckOwner);
                RunOwnerWatchdogRecovery();
                return;
            }
        }
        finally {
            Volatile.Write(ref _isOwnerWatchdogRunning, 0);
            ArmOwnerWatchdog();
        }
    }

    private static bool IsOwnerStuck()
    {
        // Every PTT callback republishes the owner, so an old stamp means none arrived.
        if (OwnerHeldFor() < OwnerWatchdogTimeout)
            return false;

        // The idle window restarts on activity but the owner's timestamp doesn't, so sustained
        // traffic grows OwnerHeldFor without bound. Playing audio proves the session is wanted.
        var playbackActivityAt = Volatile.Read(ref _playbackActivityAt);
        if (playbackActivityAt != 0 && new CpuTimestamp(playbackActivityAt).Elapsed < PlaybackActivityTimeout)
            return false;

        // A live recorder may only defer the revert, never cancel it: the latch is cleared by
        // AppleAudioCapture's finally, and an abandoned enumerator would otherwise disable this
        // insurance for the rest of the process.
        return AppleAudioCapture.InputNodeHeldFor is not { } recordingFor
            || recordingFor >= OwnerWatchdogTimeout;
    }

    private static AudioSessionOwner? TryRevertOwner()
    {
        lock (OwnerLock) {
            // Re-read under the lock: a real wake may have published a fresh owner while the
            // checks above were running, and reverting on top of it would leave the framework
            // owning a live session that the app believes is its own.
            var owner = Owner;
            if (owner == AudioSessionOwner.App || OwnerHeldFor() < OwnerWatchdogTimeout)
                return null;

            PublishOwnerUnsafe(AudioSessionOwner.App);
            return owner;
        }
    }

    private static void RunOwnerWatchdogRecovery()
    {
        // The revert only fixes the app's view: the framework still shows a transmit or a receive,
        // and a participant left set makes the next TransmitEnded hand the session back to it.
        try {
            Volatile.Read(ref _ownerWatchdogRecovery)?.Invoke();
        }
        catch (Exception e) {
            OwnerLog.LogWarning(e, "The audio session owner watchdog couldn't reset the PTT framework state");
        }
    }

    private static TimeSpan OwnerHeldFor()
        => new CpuTimestamp(Volatile.Read(ref _ownerChangedAt)).Elapsed;

    private AudioSessionSetup ReactivateUnsafe(AudioFocusMode mode)
    {
        // Under a PTT owner the framework owns category and mode too - configuring underneath it
        // is what the typed owner exists to prevent, except where the app only raises the category
        // for its own mic.
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        var isConfigured = AudioSessionOwnership.MayConfigure(owner, mode);
        if (isConfigured)
            ConfigureUnsafe(session, mode);
        if (!AudioSessionOwnership.MayActivate(owner))
            return new AudioSessionSetup(isConfigured, false);

        if (!session.SetActive(true, out var error)) {
            Log.LogWarning("Failed to re-activate audio session: {Error}", error.LocalizedDescription);
            // Deactivate and retry
            var deactivateOptions = mode is AudioFocusMode.Tune
                ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
                : 0;
            session.SetActive(false, deactivateOptions, out _);
            session.SetActive(true, out error);
            error.Assert("Failed to re-activate audio session after retry");
        }

        ApplyOutputRouteUnsafe(mode);
        return new AudioSessionSetup(isConfigured, true);
    }

    private AudioSessionSetup ReconfigureUnsafe(AudioFocusMode minMode)
    {
        // The two bits are reported separately: the framework deactivates its session without
        // telling the app's focus scopes, so a caller that read "configured" as "active" would
        // never reactivate for a still-live recording - and one that re-ran the whole mode change
        // just because activation was withheld would bounce that recording's engine.
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        if (!AudioSessionOwnership.MayActivate(owner)) {
            var isConfigured = AudioSessionOwnership.MayConfigure(owner, minMode);
            if (isConfigured) {
                // The framework's session is already active, and SetCategory on an active session
                // is what lets an in-app recording get PlayAndRecord during a live wake playback.
                ConfigureUnsafe(session, minMode);
                ApplyOutputRouteUnsafe(minMode);
            }

            return new AudioSessionSetup(isConfigured, false);
        }

        var deactivateOptions = minMode is AudioFocusMode.Tune
            ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
            : 0;
        session.SetActive(false, deactivateOptions).Assert("Failed to deactivate session");
        ConfigureUnsafe(session, minMode);
        session.SetActive(true).Assert("Failed to activate session");
        ApplyOutputRouteUnsafe(minMode);
        return new AudioSessionSetup(true, true);
    }

    private void DeactivateUnsafe()
    {
        // An active session keeps mediaserverd's voice path - and corespeechd behind it -
        // running for as long as it's held, whether or not anything is attached to it. Measured
        // on an iPhone 13 Pro: audiomxd sits at 0.27 cores after a call with every engine
        // verifiably stopped, and is absent both before the first call and once the app exits.
        if (!AudioSessionOwnership.MayActivate(Owner))
            return;

        var session = AVAudioSession.SharedInstance();
        if (!session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out var error))
            Log.LogWarning("Failed to deactivate audio session: {Error}", error.LocalizedDescription);
    }

    private void ApplyOutputRouteUnsafe(AudioFocusMode mode, bool mustDropOverride = false)
    {
        if (!AudioSessionOwnership.MayConfigure(Owner, mode))
            return;
        // PlayAndRecord is the only category with a route to pick: Playback and Ambient always
        // reach the speaker, and an override on either is rejected. See ConfigureUnsafe.
        if (mode is not AudioFocusMode.Recording)
            return;

        var session = AVAudioSession.SharedInstance();
        // CurrentRoute reports the speaker while our own override holds it there, which hides a
        // device that just arrived and would outrank it - the override has to go before the read.
        if (mustDropOverride)
            session.OverrideOutputAudioPort(AVAudioSessionPortOverride.None, out _);

        var outputs = session.CurrentRoute.Outputs;
        if (outputs.Length == 0) {
            Log.LogWarning("ApplyOutputRoute: no output ports found");
            return;
        }

        if (GetPortOverride(outputs) is not { } portOverride)
            return;

        var isOverridden = ForceOverride(session, portOverride, out var error);
        // None means an external device won the output, and the mic has to follow it: the override
        // moves playback only, so iOS leaves a headset that arrived mid-recording unheard.
        var input = ApplyPreferredInput(portOverride is AVAudioSessionPortOverride.None);
        Log.LogInformation(
            "ApplyOutputRoute: mode={Mode}, sessionMode={SessionMode}, {Outputs} -> {Override} -> {Result}, input={Input}",
            mode,
            session.Mode,
            Describe(outputs),
            portOverride,
            isOverridden ? Describe(session.CurrentRoute.Outputs) : $"failed: {error.LocalizedDescription}",
            input);
        return;

        string ApplyPreferredInput(bool mustPreferExternal) {
            // Anything that isn't the built-in mic is the user's own device, same rule as the
            // output side; null hands the choice back to iOS.
            var input = mustPreferExternal
                ? session.AvailableInputs?.FirstOrDefault(x => x.PortType != AVAudioSession.PortBuiltInMic)
                : null;
            if (!session.SetPreferredInput(input, out var inputError))
                Log.LogWarning("ApplyOutputRoute: failed to set preferred input: {Error}",
                    inputError.LocalizedDescription);
            return input?.PortType ?? "default";
        }

        static string Describe(AVAudioSessionPortDescription[] outputs)
            => string.Join(", ", outputs.Select(x => x.PortType));

        // Restating an override the session already holds is a no-op, and a no-op won't move a
        // source started after VoiceProcessingIO. Clearing first makes it a real transition.
        static bool ForceOverride(AVAudioSession session, AVAudioSessionPortOverride portOverride, out NSError error) {
            if (portOverride is AVAudioSessionPortOverride.None)
                return session.OverrideOutputAudioPort(portOverride, out error);

            return session.OverrideOutputAudioPort(AVAudioSessionPortOverride.None, out error)
                && session.OverrideOutputAudioPort(portOverride, out error);
        }

        // Null means there's nothing to state - which covers Macs, where there's no receiver to
        // be pushed off.
        static AVAudioSessionPortOverride? GetPortOverride(AVAudioSessionPortDescription[] outputs) {
            // An external device is the user's own choice of where to listen, so it outranks the
            // speaker default - and clearing the override is also what hands the route back to a
            // headset plugged in while the speaker was forced.
            if (outputs.Any(x => IsExternalPort(x.PortType)))
                return AVAudioSessionPortOverride.None;

            // Unconditional: the session reports the speaker even while a post-VPIO source plays
            // on the receiver, so the old "only if I see the receiver" test never once fired.
            return OperatingSystem.IsMacCatalyst()
                ? null
                : AVAudioSessionPortOverride.Speaker;
        }
    }

    private static bool IsExternalPort(NSString portType)
        => portType == AVAudioSession.PortBluetoothA2DP
        || portType == AVAudioSession.PortBluetoothHfp
        || portType == AVAudioSession.PortBluetoothLE
        || portType == AVAudioSession.PortHeadphones
        || portType == AVAudioSession.PortUsbAudio
        || portType == AVAudioSession.PortCarAudio
        || portType == AVAudioSession.PortHdmi
        || portType == AVAudioSession.PortAirPlay;

    private void ConfigureUnsafe(AVAudioSession session, AudioFocusMode mode)
    {
        Log.LogInformation("Configure: mode={Mode}", mode);
        if (mode is AudioFocusMode.Recording) {
            // VoiceChat carries the PTT call's AEC under a PTT owner. VideoChat, not Default, for
            // ours: SetVoiceProcessingEnabled replaces Default and drops DefaultToSpeaker with it.
            var sessionMode = Owner == AudioSessionOwner.App
                ? AVAudioSessionMode.VideoChat
                : AVAudioSessionMode.VoiceChat;
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                    sessionMode,
                    AVAudioSessionCategoryOptions.DefaultToSpeaker
                    | AVAudioSessionCategoryOptions.AllowBluetooth
                    | AVAudioSessionCategoryOptions.AllowBluetoothA2DP)
                .Assert($"{mode}: failed to set category");
            session.SetPreferredIOBufferDuration(Constants.Audio.OpusFrameDuration.TotalSeconds, out var error);
            error.Assert("Failed to set preferred IO buffer duration");
        }
        else if (mode is AudioFocusMode.Playback)
            session.SetCategory(AVAudioSessionCategory.Playback).Assert($"{mode}: failed to set category");
        else
            session.SetCategory(AVAudioSessionCategory.Ambient).Assert($"{mode}: failed to set category");
    }
}

/// <summary>
/// What a <see cref="AudioSession.Reconfigure"/> / <see cref="AudioSession.Reactivate"/> call
/// actually achieved. Under a PTT owner the app may configure the session without being allowed
/// to activate it, so the two have to be tracked apart.
/// </summary>
public readonly record struct AudioSessionSetup(bool IsConfigured, bool IsActivated);
