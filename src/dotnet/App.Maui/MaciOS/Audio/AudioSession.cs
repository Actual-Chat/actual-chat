using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;
using UIKit;

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
    private static readonly TimeSpan PttActivationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CallActivationTimeout = TimeSpan.FromSeconds(5);
    private static readonly AudioOutputRoute PhoneRoute =
        new(AudioOutputRoute.PhoneId, AudioOutputKind.Phone);
    private static readonly AudioOutputRoute SpeakerRoute =
        new(AudioOutputRoute.SpeakerId, AudioOutputKind.Speaker);

    private static readonly Lock OwnerLock = new();
    private static int _owner;
    private static long _ownerChangedAt;
    private static int _isCallVideo;
    private static int _isCallActive;
    private static int _isCallOverrideCleared;
    private static string? _selectedOutputId;
    private static string? _unsettledOutputId;
    private static AudioOutputRoute[] _externalRoutes = [];
    private static int _isBluetoothOff;
    private static int _isOwnerWatchdogRunning;
    private static Action? _ownerWatchdogRecovery;
    private static Func<bool>? _isPttActivationAvailable;
    private static Func<Task<bool>>? _pttActivationRequester;
    private static Action? _pttPlaybackRelease;
    private static Task<bool>? _pttActivationTask;
    private static TaskCompletionSource<bool>? _callActivationSource;
    private static int _isPttReleasePending;
    private static long _playbackActivityAt;

    private AppUIHub Hub { get; } = hub;
    private static ILogger OwnerLog => field ??= StaticLog.For(typeof(AudioSession));
    private ILogger Log => field ??= Hub.LogFor(GetType());
    public static AudioSessionOwner Owner => (AudioSessionOwner)Volatile.Read(ref _owner);
    public static bool MayActivateNow => AudioSessionOwnership.MayActivate(Owner);

    public static bool IsCallVideo {
        get => Volatile.Read(ref _isCallVideo) != 0;
        set => Volatile.Write(ref _isCallVideo, value ? 1 : 0);
    }

    public static bool IsCallActive {
        get => Volatile.Read(ref _isCallActive) != 0;
        set => Volatile.Write(ref _isCallActive, value ? 1 : 0);
    }

    public static void SetOwner(AudioSessionOwner owner)
        => PublishOwner(owner);

    public static void ReleaseOwner(AudioSessionRelease release, bool hasLivePlayback = false)
    {
        lock (OwnerLock) {
            // The latch scopes to one CallKit call: the next one must clear the route again,
            // since a stale Speaker override may again be sitting there from pre-call recording.
            var wasCallKit = Owner == AudioSessionOwner.CallKit;
            PublishOwnerUnsafe(AudioSessionOwnership.OnReleased(Owner, release, hasLivePlayback));
            if (wasCallKit) {
                ResetCallRouteLatch();
                ResetCallActivationUnsafe();
            }
        }

        ArmOwnerWatchdog();
    }

    public static void ResetCallRouteLatch()
    {
        // ReleaseOwner clears the latch only while CallKit still owns the session, so a call PTT
        // took the session away from mid-way would leave the next one on this one's route.
        // The user's output pick is this call's too: the next one starts from the defaults.
        Volatile.Write(ref _isCallOverrideCleared, 0);
        Volatile.Write(ref _selectedOutputId, null);
        Volatile.Write(ref _unsettledOutputId, null);
        Volatile.Write(ref _externalRoutes, []);
        Volatile.Write(ref _isBluetoothOff, 0);
    }

    public static void SetOwnerWatchdogRecovery(Action recovery)
        => Volatile.Write(ref _ownerWatchdogRecovery, recovery);

    public static void SetPttPlaybackHooks(
        Func<bool> isActivationAvailable, Func<Task<bool>> requestActivation, Action releaseActivation)
    {
        Volatile.Write(ref _isPttActivationAvailable, isActivationAvailable);
        Volatile.Write(ref _pttActivationRequester, requestActivation);
        Volatile.Write(ref _pttPlaybackRelease, releaseActivation);
    }

    public static Task<bool> WhenActivatedByOwner()
    {
        // CallKit activates the session itself once the answer or start action is fulfilled, and
        // reports that through DidActivateAudioSession: there is nothing to request, only a wait.
        if (Owner == AudioSessionOwner.CallKit)
            return WhenCallSessionActivated();

        // An app joined to its PTT channel may not activate its own session in the background -
        // SetActive answers CannotInterruptOthers - but the framework activates it for a set
        // participant, and reports that through DidActivateAudioSession. One request serves
        // every caller that runs into the refusal meanwhile - including one that arrives after
        // the activation already happened: the framework activates once per participant, so a
        // second request would wait for a callback that never comes. A PTT owner therefore
        // answers "active" - unless its release is still in flight, in which case the session
        // is on its way down and only a fresh participant brings it back.
        if (Volatile.Read(ref _pttActivationRequester) is not { } requester)
            return ActualLab.Async.TaskExt.FalseTask;

        TaskCompletionSource<bool> source;
        lock (OwnerLock) {
            if (_pttActivationTask is { IsCompleted: false } pending)
                return pending;
            if (Owner != AudioSessionOwner.App && Volatile.Read(ref _isPttReleasePending) == 0)
                return ActualLab.Async.TaskExt.TrueTask;

            source = TaskCompletionSourceExt.New<bool>();
            _pttActivationTask = source.Task;
        }
        // The availability check takes the PTT lock, which SetOwner is called under - so it, like
        // the request itself, stays outside OwnerLock.
        if (!IsPttActivationAvailable) {
            source.TrySetResult(false);
            return source.Task;
        }

        _ = BackgroundTask.Run(async () => {
            var isActivated = false;
            try {
                isActivated = await requester.Invoke().WaitAsync(PttActivationTimeout).ConfigureAwait(false);
            }
            catch (Exception e) {
                OwnerLog.LogWarning(e, "The PTT framework didn't activate the audio session");
            }
            // The participant the request set would otherwise keep the framework's "receiving"
            // state, and its flag, until the owner watchdog fires.
            if (!isActivated && Owner == AudioSessionOwner.App)
                ReleasePttPlayback();
            source.TrySetResult(isActivated);
        }, OwnerLog, "PTT activation request failed", CancellationToken.None);
        return source.Task;
    }

    private static bool IsPttActivationAvailable
        => Volatile.Read(ref _isPttActivationAvailable)?.Invoke() == true;

    private static bool IsPttActivationPending
        => Volatile.Read(ref _pttActivationTask) is { IsCompleted: false };

    private static bool IsCallSessionActive
        => Volatile.Read(ref _callActivationSource) is { Task: { IsCompletedSuccessfully: true, Result: true } };

    private static async Task<bool> WhenCallSessionActivated()
    {
        // Bounded: a callback that never comes has to fail the start, not hang it.
        if (Volatile.Read(ref _callActivationSource) is not { } source)
            return false;

        try {
            return await source.Task.WaitAsync(CallActivationTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            OwnerLog.LogWarning("CallKit didn't activate the audio session in {Timeout}",
                CallActivationTimeout.ToShortString());
            return false;
        }
    }

    private static void ResetCallActivationUnsafe()
    {
        _callActivationSource?.TrySetResult(false);
        // Publication: IsCallSessionActive and WhenCallSessionActivated read it without the lock.
        Volatile.Write(ref _callActivationSource, null);
    }

    private static void ReleasePttPlayback()
    {
        // Never lets a throw out: the request's completion, and DeactivateUnsafe, sit behind it.
        // The flag stays up until the owner comes back to App (DidDeactivateAudioSession), so a
        // request landing in between asks for a fresh participant instead of trusting the owner.
        if (Owner != AudioSessionOwner.App)
            Volatile.Write(ref _isPttReleasePending, 1);
        try {
            Volatile.Read(ref _pttPlaybackRelease)?.Invoke();
        }
        catch (Exception e) {
            OwnerLog.LogWarning(e, "Couldn't release the PTT playback participant");
        }
    }

    public static void PrepareForCall()
    {
        // Before the answer or start action is fulfilled, since CallKit activates the session with
        // the category it finds - see PrepareForSession. Owned from here, so nothing in between
        // activates the session on its own and races the framework.
        lock (OwnerLock) {
            _callActivationSource?.TrySetResult(false);
            // Publication: read without the lock.
            Volatile.Write(ref _callActivationSource, TaskCompletionSourceExt.New<bool>());
            PublishOwnerUnsafe(AudioSessionOwner.CallKit);
        }
        PrepareForSession(AudioSessionOwner.CallKit);
    }

    public static void OnCallSessionActivated()
    {
        lock (OwnerLock) {
            PublishOwnerUnsafe(AudioSessionOwner.CallKit);
            // Armed by PrepareForCall; a callback with nothing armed still marks the session active.
            var source = _callActivationSource ?? TaskCompletionSourceExt.New<bool>();
            source.TrySetResult(true);
            // Publication: read without the lock.
            Volatile.Write(ref _callActivationSource, source);
        }
    }

    public static void PrepareForSession(AudioSessionOwner owner)
    {
        // Before the PTT framework or CallKit activates a session: it takes the category as it
        // finds it. A listening burst leaves Playback, whose input node reports no sample rate,
        // and an idle app leaves Ambient, which is mixable and which neither framework activates
        // in the background at all - the activation callback then never comes.
        try {
            ConfigureRecordingUnsafe(AVAudioSession.SharedInstance(), owner, IsCallVideo);
            OwnerLog.LogInformation("Session prepared for {Owner}", owner);
        }
        catch (Exception e) {
            OwnerLog.LogWarning(e, "Couldn't prepare the session for {Owner}", owner);
        }
    }

    public static bool IsActivationRefused(NSError error)
        // CannotInterruptOthers ('!int') for an app that just went to the background,
        // MissingEntitlement ('ent?') for one resumed from suspension - both mean the app may
        // not activate its own session there and only the PTT framework can.
        => (AVAudioSessionErrorCode)error.Code
            is AVAudioSessionErrorCode.CannotInterruptOthers or AVAudioSessionErrorCode.MissingEntitlement;

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

    // mustForce restates an override the route already shows - a source started after
    // VoiceProcessingIO needs that, an incidental route change does not.
    public Task ApplyOutputRoute(AudioFocusMode mode, bool mustDropOverride = false, bool mustForce = false)
        => DispatchToMainThread(() => ApplyOutputRouteUnsafe(mode, mustDropOverride || mustForce, mustDropOverride));

    public Task<AudioOutputRoutes> GetOutputRoutes()
        => DispatchToMainThread(GetOutputRoutesUnsafe);

    public static bool HasSelectedOutput => Volatile.Read(ref _selectedOutputId) is not null;

    public Task ClearOutputRoute(AudioFocusMode mode)
        => DispatchToMainThread(() => {
            Volatile.Write(ref _selectedOutputId, null);
            Volatile.Write(ref _unsettledOutputId, null);
            Volatile.Write(ref _isBluetoothOff, 0);
            ApplyOutputRouteUnsafe(mode);
        });

    public Task SelectOutputRoute(string routeId, AudioFocusMode mode)
        => DispatchToMainThread(() => {
            Volatile.Write(ref _selectedOutputId, routeId);
            Volatile.Write(ref _unsettledOutputId, null);
            // The earpiece switches Bluetooth off, and the speaker leaves it that way: the speaker
            // override beats a device either way, and turning Bluetooth back on reconnects the
            // headset first - about a second more on every earpiece-to-speaker switch.
            if (routeId == AudioOutputRoute.PhoneId && Volatile.Read(ref _externalRoutes).Length != 0)
                Volatile.Write(ref _isBluetoothOff, 1);
            else if (routeId != AudioOutputRoute.SpeakerId)
                Volatile.Write(ref _isBluetoothOff, 0);
            ApplyOutputRouteUnsafe(mode, mustForce: true);
        });

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

    public AudioOutputKind? GetCurrentOutputKind()
    {
        try {
            return AVAudioSession.SharedInstance().CurrentRoute.Outputs.FirstOrDefault()?.GetOutputKind();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to read the audio output kind");
            return null;
        }
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
        if (owner == AudioSessionOwner.App)
            Volatile.Write(ref _isPttReleasePending, 0);
    }

    private static void ArmOwnerWatchdog()
    {
        // CallKit polices its own lifecycle via DidDeactivateAudioSession/DidReset, and the
        // recovery action below resets PTT framework state - it must never run for a live call.
        if (Owner is AudioSessionOwner.App or AudioSessionOwner.CallKit)
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
                // The recovery resets PTT framework state, which would tear down a live call: the
                // arm race can land here for a CallKit owner, and CallKit polices its own lifecycle.
                if (stuckOwner != AudioSessionOwner.CallKit)
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
            // A rare arm-race (a watchdog already running for a PTT owner, still polling right as
            // that owner hands off to CallKit) can still land here for a CallKit owner - keep the
            // latch scoped to one call on this exit path too.
            if (owner == AudioSessionOwner.CallKit) {
                ResetCallRouteLatch();
                ResetCallActivationUnsafe();
            }
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
        if (isConfigured && !TryConfigureUnsafe(session, mode))
            return new AudioSessionSetup(false, false, true);
        if (!AudioSessionOwnership.MayActivate(owner)) {
            // The route is applied once the session is active - see WaitForOwnerActivation.
            var isActivationPending = owner == AudioSessionOwner.CallKit && !IsCallSessionActive;
            if (!isActivationPending)
                ApplyOutputRouteUnsafe(mode);
            Log.LogInformation("Reactivate({Mode}) under {Owner}: configured={IsConfigured}, {Session}",
                mode, owner, isConfigured, session.Describe());
            return new AudioSessionSetup(isConfigured, false, isActivationPending);
        }

        if (!session.SetActive(true, out var error)) {
            if (TryAwaitOwnerActivation(error, mode))
                return new AudioSessionSetup(isConfigured, false, true);

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
            // The framework's session is already active, and SetCategory on an active session
            // is what lets an in-app recording get PlayAndRecord during a live wake playback.
            if (isConfigured && !TryConfigureUnsafe(session, minMode))
                return new AudioSessionSetup(false, false, true);
            var isActivationPending = owner == AudioSessionOwner.CallKit && !IsCallSessionActive;
            if (!isActivationPending)
                ApplyOutputRouteUnsafe(minMode);
            Log.LogInformation("Reconfigure({Mode}) under {Owner}: configured={IsConfigured}, {Session}",
                minMode, owner, isConfigured, session.Describe());
            return new AudioSessionSetup(isConfigured, false, isActivationPending);
        }

        var deactivateOptions = minMode is AudioFocusMode.Tune
            ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
            : 0;
        session.SetActive(false, deactivateOptions).Assert("Failed to deactivate session");
        if (!TryConfigureUnsafe(session, minMode))
            return new AudioSessionSetup(false, false, true);
        if (!session.SetActive(true, out var error)) {
            if (TryAwaitOwnerActivation(error, minMode))
                return new AudioSessionSetup(true, false, true);

            error.Assert("Failed to activate session");
        }
        ApplyOutputRouteUnsafe(minMode);
        return new AudioSessionSetup(true, true);
    }

    private bool TryConfigureUnsafe(AVAudioSession session, AudioFocusMode mode)
    {
        // SetCategory is refused on the same terms as SetActive - a PTT-joined app in the
        // background just after the framework let its session go - and the answer is the same:
        // ask the framework, which configures on the way in.
        try {
            ConfigureUnsafe(session, mode);
            return true;
        }
        catch (Exception e) when (e.InnerException is NSErrorException { Error: { } nsError }
            && TryAwaitOwnerActivation(nsError, mode)) {
            return false;
        }
    }

    private bool TryAwaitOwnerActivation(NSError error, AudioFocusMode mode)
    {
        if (!IsActivationRefused(error))
            return false;

        // A refusal during a CallKit call is the activation still on its way; every mode waits for it.
        if (Owner == AudioSessionOwner.CallKit) {
            Log.LogInformation("Activate({Mode}): refused ({Error}), waiting for CallKit to activate the session",
                mode, error.LocalizedDescription);
            return true;
        }

        // Without a joined PTT channel the refusal is somebody else's non-mixable session. A
        // recording is not asked for either: the framework would show it as an incoming receive,
        // and the app's own mic in the background is a transmit's business, not this path's.
        if (mode is AudioFocusMode.Recording || !IsPttActivationAvailable)
            return false;

        Log.LogInformation(
            "Activate({Mode}): refused ({Error}), asking the PTT framework to activate the session",
            mode, error.LocalizedDescription);
        _ = WhenActivatedByOwner();
        return true;
    }

    private void DeactivateUnsafe()
    {
        // An active session keeps mediaserverd's voice path - and corespeechd behind it -
        // running for as long as it's held, whether or not anything is attached to it. Measured
        // on an iPhone 13 Pro: audiomxd sits at 0.27 cores after a call with every engine
        // verifiably stopped, and is absent both before the first call and once the app exits.
        var owner = Owner;
        if (!AudioSessionOwnership.MayActivate(owner)) {
            // Nothing wants the session any more: a playback the framework activated goes back to
            // it here, or the "receiving" it shows stays up until the owner watchdog fires.
            if (owner == AudioSessionOwner.PttPlayback)
                ReleasePttPlayback();
            return;
        }

        // A request still in flight would activate a session nothing wants any more, with no
        // scope left to hand it back.
        if (IsPttActivationPending)
            ReleasePttPlayback();
        var session = AVAudioSession.SharedInstance();
        if (!session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out var error))
            Log.LogWarning("Failed to deactivate audio session: {Error}", error.LocalizedDescription);
    }

    private void ApplyOutputRouteUnsafe(
        AudioFocusMode mode, bool mustForce = false, bool mustDropOverride = false)
    {
        // PlayAndRecord is the only category with a route to pick: Playback and Ambient always
        // reach the speaker, and an override on either is rejected. The category decides, not
        // the mode or the owner: a session the PTT framework activated is PlayAndRecord whatever
        // the app plays on it, and it lands on the receiver until the app overrides the port.
        var session = AVAudioSession.SharedInstance();
        if (session.GetCategory() != AVAudioSessionCategory.PlayAndRecord)
            return;

        // CurrentRoute reports the speaker while our own override holds it there, which hides a
        // device that just arrived and would outrank it - the override has to go before the read.
        if (mustDropOverride) {
            session.OverrideOutputAudioPort(AVAudioSessionPortOverride.None, out _);
            // A device that just arrived is where the user now wants to listen, over any earlier pick.
            Volatile.Write(ref _selectedOutputId, null);
            Volatile.Write(ref _isBluetoothOff, 0);
        }
        if (TryApplySelectedOutputUnsafe(session, mode, mustForce))
            return;

        var outputs = session.CurrentRoute.Outputs;
        if (outputs.Length == 0) {
            Log.LogWarning("ApplyOutputRoute: no output ports found");
            return;
        }

        var hasExternalDevice = outputs.Any(x => IsExternalPort(x.PortType));
        if (GetPortOverride(hasExternalDevice, MustPreferSpeaker(Owner, IsCallVideo)) is not { } portOverride)
            return;

        if (!mustForce
            && portOverride is AVAudioSessionPortOverride.Speaker
            && IsOnRoute(session, SpeakerRoute.Id))
            return;

        // An external device won the output, and the mic has to follow it: the override moves
        // playback only, so iOS leaves a headset that arrived mid-recording unheard. The input
        // goes first - changing it re-routes the session, which drops an override set before it.
        var input = ApplyPreferredInput(hasExternalDevice);
        var isOverridden = ForceOverride(session, portOverride, out var error);
        Log.LogInformation(
            "ApplyOutputRoute: mode={Mode}, sessionMode={SessionMode}, "
            + "{Outputs} -> {Override} -> {Result}, input={Input}",
            mode,
            session.Mode,
            outputs.Describe(),
            portOverride,
            isOverridden ? session.CurrentRoute.Outputs.Describe() : $"failed: {error.LocalizedDescription}",
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

        // Null means leave the route alone - Macs have no receiver to push off, and once a live
        // CallKit voice call's stale override is cleared, CallKit and the user's toggle own it.
        static AVAudioSessionPortOverride? GetPortOverride(bool hasExternalDevice, bool mustPreferSpeaker) {
            // An external device is the user's own choice of where to listen, so it outranks the
            // speaker default - and clearing the override is also what hands the route back to a
            // headset plugged in while the speaker was forced.
            if (hasExternalDevice)
                return AVAudioSessionPortOverride.None;

            if (!mustPreferSpeaker) {
                // CallKit and the user's speaker toggle own the route from here; a stale Speaker
                // override from pre-call recording is cleared exactly once, then left untouched.
                if (Interlocked.Exchange(ref _isCallOverrideCleared, 1) != 0)
                    return null;

                return AVAudioSessionPortOverride.None;
            }

            // Forced only when the speaker is wanted: the session reports the speaker even while
            // a post-VPIO source plays on the receiver, so an "only if I see it" test never fires.
            return OperatingSystem.IsMacCatalyst()
                ? null
                : AVAudioSessionPortOverride.Speaker;
        }
    }

    private bool TryApplySelectedOutputUnsafe(AVAudioSession session, AudioFocusMode mode, bool mustForce)
    {
        if (Volatile.Read(ref _selectedOutputId) is not { } selectedId)
            return false;

        // Restating a route the session already holds still stops and rebuilds the engine, whose
        // rebuild raises the next route change - the loop that starves the recorder mid-call.
        if (!mustForce && IsOnRoute(session, selectedId))
            return true;

        // A pick iOS won't grant - the earpiece behind a device that outranks it - is applied once
        // and then left: re-applying it raises another route change, which lands back here.
        if (!mustForce && Volatile.Read(ref _unsettledOutputId) == selectedId)
            return true;

        var builtInMic = session.AvailableInputs?.FirstOrDefault(x => x.PortType == AVAudioSession.PortBuiltInMic);
        AVAudioSessionPortOverride portOverride;
        AVAudioSessionPortDescription? input;
        // The mode and the options decide the route before any override does, and both read the
        // pick - the earpiece drops AllowBluetooth, without which a device keeps the output. Only
        // a fresh pick rebuilds on an option difference: iOS re-adds bits of its own under some
        // modes, and chasing those on every route change is a loop.
        var sessionMode = GetSessionMode(Owner, IsCallVideo);
        var options = GetCategoryOptions(Owner, IsCallVideo);
        if (!IsConfiguredAs(session, sessionMode, options) && (mustForce || session.GetMode() != sessionMode)) {
            Log.LogInformation(
                "ApplyOutputRoute: reconfiguring for {Id}: mode {From} -> {To}, options {FromOptions} -> {ToOptions}",
                selectedId, session.Mode, sessionMode, session.CategoryOptions, options);
            // A refused category change costs this pick only; the caller's playback still starts.
            try {
                ConfigureRecordingUnsafe(session, Owner, IsCallVideo);
            }
            catch (Exception e) {
                Log.LogWarning(e, "ApplyOutputRoute: couldn't reconfigure the session for {Id}", selectedId);
            }
        }

        if (selectedId == SpeakerRoute.Id)
            (portOverride, input) = (AVAudioSessionPortOverride.Speaker, builtInMic);
        else if (selectedId == PhoneRoute.Id)
            // The input leads for a two-way device: moving it off a headset's mic brings the
            // output back to the receiver too.
            (portOverride, input) = (AVAudioSessionPortOverride.None, builtInMic);
        else {
            input = session.AvailableInputs?.FirstOrDefault(x => x.UID == selectedId);
            // An output-only device (A2DP, AirPlay, wired headphones) is reached by clearing the
            // override and leaving the input to iOS; one that's gone ends the pick.
            var isOutputOnlyCurrent = input is null && session.CurrentRoute.Outputs.Any(x => x.UID == selectedId);
            if (input is null && !isOutputOnlyCurrent) {
                Log.LogInformation("ApplyOutputRoute: selected output {Id} is gone, back to the defaults", selectedId);
                Volatile.Write(ref _selectedOutputId, null);
                Volatile.Write(ref _isBluetoothOff, 0);
                return false;
            }

            portOverride = AVAudioSessionPortOverride.None;
        }

        var outputChannels = session.OutputNumberOfChannels;
        // The input goes first: changing it re-routes the session, which drops an override set
        // before it - the first speaker tap of a call landed back on the earpiece. Restating the
        // input it already has still raises a route change that lands back here, so it's skipped.
        if (session.PreferredInput?.UID != input?.UID && !session.SetPreferredInput(input, out var inputError))
            Log.LogWarning("ApplyOutputRoute: failed to set preferred input: {Error}", inputError.LocalizedDescription);
        var isOverridden = ForceOverride(session, portOverride, out var error);
        Log.LogInformation(
            "ApplyOutputRoute: mode={Mode}, selected={Selected} -> {Result}, input={Input}, "
            + "outChannels={Before}->{After}",
            mode,
            selectedId,
            isOverridden ? session.CurrentRoute.Outputs.Describe() : $"failed: {error.LocalizedDescription}",
            input?.PortType ?? "default",
            outputChannels,
            session.OutputNumberOfChannels);
        var isSettled = IsOnRoute(session, selectedId);
        Volatile.Write(ref _unsettledOutputId, isSettled ? null : selectedId);
        Log.LogInformation("ApplyOutputRoute: after {Selected}: settled={IsSettled}, options={Options}, {Session}",
            selectedId, isSettled, session.CategoryOptions, session.DescribeFormat());
        return true;
    }

    private static bool IsOnRoute(AVAudioSession session, string routeId)
    {
        var outputs = session.CurrentRoute.Outputs;
        if (routeId == SpeakerRoute.Id)
            return outputs.Any(x => x.PortType == AVAudioSession.PortBuiltInSpeaker);
        if (routeId == PhoneRoute.Id)
            return outputs.Any(x => x.PortType == AVAudioSession.PortBuiltInReceiver);

        return outputs.Any(x => x.UID == routeId);
    }

    private static bool ForceOverride(
        AVAudioSession session, AVAudioSessionPortOverride portOverride, out NSError error)
    {
        // Restating an override the session already holds is a no-op, and a no-op won't move a
        // source started after VoiceProcessingIO. Clearing first makes it a real transition.
        if (portOverride is AVAudioSessionPortOverride.None)
            return session.OverrideOutputAudioPort(portOverride, out error);

        return session.OverrideOutputAudioPort(AVAudioSessionPortOverride.None, out error)
            && session.OverrideOutputAudioPort(portOverride, out error);
    }

    private static AudioOutputRoutes GetOutputRoutesUnsafe()
    {
        // Only PlayAndRecord has a route to pick, and a Mac has no receiver and no override.
        var session = AVAudioSession.SharedInstance();
        if (OperatingSystem.IsMacCatalyst()
            || session.GetCategory() != AVAudioSessionCategory.PlayAndRecord)
            return AudioOutputRoutes.None;

        // iOS lists inputs, never outputs: a two-way device shows up as its mic, and an
        // output-only one only while it's the current output.
        var outputs = session.CurrentRoute.Outputs;
        var routes = new List<AudioOutputRoute>();
        var hasReceiver = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone;
        // Wired headphones take the output from the receiver, and there's no switching back.
        if (hasReceiver && outputs.All(x => x.PortType != AVAudioSession.PortHeadphones))
            routes.Add(PhoneRoute);
        routes.Add(SpeakerRoute);
        foreach (var input in session.AvailableInputs ?? []) {
            if (input.PortType != AVAudioSession.PortBuiltInMic)
                routes.Add(ToRoute(input));
        }

        // While Bluetooth is switched off iOS reports no device at all - so the ones seen just
        // before the earpiece pick are carried over, or the device would drop off the menu the
        // moment it's stepped away from and there'd be no way back to it. One unplugged
        // meanwhile lingers until the next pick, which then fails and clears it.
        if (Volatile.Read(ref _isBluetoothOff) != 0)
            routes.AddRange(Volatile.Read(ref _externalRoutes).Where(x => routes.All(y => y.Id != x.Id)));
        else
            Volatile.Write(ref _externalRoutes,
                routes.Where(x => x.Kind is not (AudioOutputKind.Phone or AudioOutputKind.Speaker)).ToArray());

        var current = outputs.FirstOrDefault();
        if (current is null)
            return new AudioOutputRoutes(routes, "");

        var currentId = GetRouteId(current, routes);
        if (currentId is null && IsExternalPort(current.PortType)) {
            var route = ToRoute(current);
            routes.Add(route);
            currentId = route.Id;
        }
        return new AudioOutputRoutes(routes, currentId ?? "");

        static string? GetRouteId(AVAudioSessionPortDescription output, List<AudioOutputRoute> routes) {
            if (output.PortType == AVAudioSession.PortBuiltInReceiver)
                return PhoneRoute.Id;
            if (output.PortType == AVAudioSession.PortBuiltInSpeaker)
                return SpeakerRoute.Id;

            // A headset's mic and its output are two ports with different UIDs and names.
            // TODO: looks like a candidate for a  AVAudioSessionPortDescriptionExt
            var kind = output.GetOutputKind();
            var route = routes.FirstOrDefault(x => x.Id == output.UID)
                ?? routes.FirstOrDefault(x => x.Kind == kind
                    && (kind == AudioOutputKind.Headphones || x.Name == output.PortName));
            return route?.Id;
        }

        static AudioOutputRoute ToRoute(AVAudioSessionPortDescription port) {
            // A wired port's name is "Headset Microphone" or "Headphones" - the kind says it better.
            var kind = port.GetOutputKind();
            return new AudioOutputRoute(port.UID, kind, kind == AudioOutputKind.Headphones ? "" : port.PortName);
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
        // A call on the line keeps the call's category whatever the focus mode: Playback and
        // Ambient only ever reach the loudspeaker, so a muted call would lose the earpiece.
        if (mode is AudioFocusMode.Recording || IsCallActive)
            ConfigureRecordingUnsafe(session, Owner, IsCallVideo);
        else if (mode is AudioFocusMode.Playback or AudioFocusMode.Listening)
            session.SetCategory(AVAudioSessionCategory.Playback).Assert($"{mode}: failed to set category");
        else
            session.SetCategory(AVAudioSessionCategory.Ambient).Assert($"{mode}: failed to set category");
    }

    private static void ConfigureRecordingUnsafe(AVAudioSession session, AudioSessionOwner owner, bool isCallVideo)
    {
        var sessionMode = GetSessionMode(owner, isCallVideo);
        var options = GetCategoryOptions(owner, isCallVideo);
        // Even identical values count as a change on a live session: they clear the port override
        // and rebuild the voice path, so a mute mid-call would drop the call off the speaker.
        if (IsConfiguredAs(session, sessionMode, options))
            return;

        session.SetCategory(AVAudioSessionCategory.PlayAndRecord, sessionMode, options)
            .Assert("Recording: failed to set category");
        session.SetPreferredIOBufferDuration(Constants.Audio.OpusFrameDuration.TotalSeconds, out var error);
        error.Assert("Failed to set preferred IO buffer duration");
    }

    private static AVAudioSessionMode GetSessionMode(AudioSessionOwner owner, bool isCallVideo)
    {
        // VideoChat always takes the loudspeaker - an override can't pull it back - so the
        // earpiece needs VoiceChat, the telephony profile, whoever owns the session. A live call
        // stays in it throughout, so a switch never swaps the mode on top of the output change.
        if (IsCallActive || Volatile.Read(ref _selectedOutputId) == AudioOutputRoute.PhoneId)
            return AVAudioSessionMode.VoiceChat;

        // VoiceChat carries the PTT call's AEC under a PTT owner. VideoChat, not Default, for
        // ours: SetVoiceProcessingEnabled replaces Default and drops DefaultToSpeaker with it.
        // Default for a PTT playback: VoiceChat puts the loudspeaker on the telephony profile and
        // the call volume, far too quiet for a hands-free listener - and a wake plays, it doesn't
        // record, so it wants no AEC. A transmit re-prepares with VoiceChat when it begins.
        return owner switch {
            AudioSessionOwner.App => AVAudioSessionMode.VideoChat,
            AudioSessionOwner.CallKit when isCallVideo => AVAudioSessionMode.VideoChat,
            AudioSessionOwner.PttPlayback => AVAudioSessionMode.Default,
            _ => AVAudioSessionMode.VoiceChat,
        };
    }

    private static bool IsConfiguredAs(
        AVAudioSession session, AVAudioSessionMode mode, AVAudioSessionCategoryOptions options)
    {
        const AVAudioSessionCategoryOptions ownOptions = AVAudioSessionCategoryOptions.DefaultToSpeaker
            | AVAudioSessionCategoryOptions.AllowBluetooth
            | AVAudioSessionCategoryOptions.AllowBluetoothA2DP;
        return session.GetCategory() == AVAudioSessionCategory.PlayAndRecord
            && session.GetMode() == mode
            && (session.CategoryOptions & ownOptions) == options;
    }

    private static AVAudioSessionCategoryOptions GetCategoryOptions(AudioSessionOwner owner, bool isCallVideo)
    {
        // A connected Bluetooth device holds the output whatever the override says, so the earpiece
        // is only reachable with the option gone, and it has to stay gone: restoring it hands the
        // route straight back. The device leaves AvailableInputs with it - hence the listing below.
        // Only with a device actually connected: dropping the option rebuilds the category, which
        // costs about 1.5s, and the earpiece needs nothing of the sort when nothing outranks it.
        // SelectOutputRoute decides when that applies.
        if (Volatile.Read(ref _isBluetoothOff) != 0)
            return default;

        var options = AVAudioSessionCategoryOptions.AllowBluetooth
            | AVAudioSessionCategoryOptions.AllowBluetoothA2DP;
        // A live call leaves the speaker to the port override: flipping DefaultToSpeaker is a
        // category change, which costs a second of rebuilt voice path on every switch.
        // A CallKit voice call is the one case that wants the receiver, so it drops
        // DefaultToSpeaker too - a phone call starts at the ear, not on the speaker.
        if (!IsCallActive && MustPreferSpeaker(owner, isCallVideo))
            options |= AVAudioSessionCategoryOptions.DefaultToSpeaker;
        return options;
    }

    private static bool MustPreferSpeaker(AudioSessionOwner owner, bool isCallVideo)
        // A call starts on the built-in output CallUI's rule names, whoever owns the session.
        // Anything else - a voice message, PTT - keeps the speaker.
        => Volatile.Read(ref _selectedOutputId) != PhoneRoute.Id
            && (!(IsCallActive || owner == AudioSessionOwner.CallKit)
                || AudioOutputRoute.GetDefaultBuiltinId(isCallVideo) == SpeakerRoute.Id);
}

/// <summary>
/// What a <see cref="AudioSession.Reconfigure"/> / <see cref="AudioSession.Reactivate"/> call
/// actually achieved. Under a PTT or CallKit owner the app may configure the session without being
/// allowed to activate it, so the two have to be tracked apart; a pending activation is one the
/// framework owes, to be awaited via <see cref="AudioSession.WhenActivatedByOwner"/>.
/// </summary>
public readonly record struct AudioSessionSetup(
    bool IsConfigured,
    bool IsActivated,
    bool IsOwnerActivationPending = false);
