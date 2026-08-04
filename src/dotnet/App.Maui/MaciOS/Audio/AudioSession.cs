using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class AudioSession(AppUIHub hub) : IAsyncDisposable
{
    // Past the hot window: while that window is open the owner legitimately stays PTT-held with
    // no callback in between, so anything shorter would revert a live walkie session.
    private static readonly TimeSpan OwnerWatchdogTimeout =
        Constants.Audio.WalkieTalkieIdleTimeout + TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OwnerWatchdogPeriod = TimeSpan.FromSeconds(30);

    private static int _owner;
    private static long _ownerChangedAt;
    private static int _isOwnerWatchdogRunning;

    public static AudioSessionOwner Owner => (AudioSessionOwner)Volatile.Read(ref _owner);

    public static void SetOwner(AudioSessionOwner owner)
        => PublishOwner(owner);

    public static void ReleaseOwner(AudioSessionRelease release, bool hasLivePlayback = false)
        => PublishOwner(AudioSessionOwnership.OnReleased(Owner, release, hasLivePlayback));

    private static ILogger OwnerLog => field ??= StaticLog.For(typeof(AudioSession));
    private ILogger Log => field ??= hub.LogFor(GetType());

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

    public Task Reconfigure(AudioFocusMode mode)
        => DispatchToMainThread(() => ReconfigureUnsafe(mode));

    public Task Reactivate(AudioFocusMode mode)
        => DispatchToMainThread(() => ReactivateUnsafe(mode));

    public Task EnsureCorrectOutputRoute()
        => DispatchToMainThread(EnsureCorrectOutputRouteUnsafe);

    public AppleAudioSessionDiagnostics? GetDiagnostics()
    {
        try {
            var session = AVAudioSession.SharedInstance();
            var outputs = session.CurrentRoute.Outputs;
            var routes = outputs.Select(output => $"{output.PortName} ({output.PortType})").ToList();
            return new AppleAudioSessionDiagnostics(session.Category, session.Mode, session.OtherAudioPlaying, routes);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to get audio session diagnostics");
            return null;
        }
    }

    // Private methods

    private static void PublishOwner(AudioSessionOwner owner)
    {
        Volatile.Write(ref _ownerChangedAt, CpuTimestamp.Now.Value);
        Volatile.Write(ref _owner, (int)owner);
        if (owner != AudioSessionOwner.App)
            EnsureOwnerWatchdog();
    }

    private static void EnsureOwnerWatchdog()
    {
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
                var owner = Owner;
                if (owner == AudioSessionOwner.App)
                    return;

                // Every PTT callback republishes the owner, so an old stamp means none arrived.
                var heldFor = new CpuTimestamp(Volatile.Read(ref _ownerChangedAt)).Elapsed;
                if (heldFor < OwnerWatchdogTimeout || AppleAudioCapture.IsInputNodeHeld)
                    continue;

                // MayActivate is false for both PTT owners, so a stuck one leaves the whole app
                // unable to activate its own session - every tune, playback and recording dies.
                OwnerLog.LogWarning(
                    "The audio session has been owned by {Owner} for {Duration} with no PTT callback - reverting to App",
                    owner,
                    heldFor.ToShortString());
                Volatile.Write(ref _owner, (int)AudioSessionOwner.App);
                return;
            }
        }
        finally {
            Volatile.Write(ref _isOwnerWatchdogRunning, 0);
            if (Owner != AudioSessionOwner.App)
                EnsureOwnerWatchdog();
        }
    }

    private void ReactivateUnsafe(AudioFocusMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        // Under either PTT owner the framework owns category and mode too - configuring underneath
        // it is what the typed owner exists to prevent.
        if (AudioSessionOwnership.MayConfigure(owner))
            ConfigureUnsafe(session, mode);
        if (!AudioSessionOwnership.MayActivate(owner))
            return;

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
    }

    private void ReconfigureUnsafe(AudioFocusMode minMode)
    {
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        if (!AudioSessionOwnership.MayActivate(owner)) {
            if (AudioSessionOwnership.MayConfigure(owner))
                ConfigureUnsafe(session, minMode);
            return;
        }

        var deactivateOptions = minMode is AudioFocusMode.Tune
            ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
            : 0;
        session.SetActive(false, deactivateOptions).Assert("Failed to deactivate session");
        ConfigureUnsafe(session, minMode);
        session.SetActive(true).Assert("Failed to activate session");
    }

    private void EnsureCorrectOutputRouteUnsafe()
    {
        var session = AVAudioSession.SharedInstance();
        var outputs = session.CurrentRoute.Outputs;
        if (outputs.Length == 0) {
            Log.LogWarning("EnsureCorrectOutputRoute: no output ports found");
            return;
        }

        // If any output is an external device, don't override — let iOS route to it
        foreach (var output in outputs) {
            if (IsExternalPort(output.PortType)) {
                Log.LogInformation("EnsureCorrectOutputRoute: external device ({PortType}), skipping", output.PortType);
                return;
            }
        }

        // If output is the receiver (earpiece), override to speaker
        foreach (var output in outputs) {
            if (output.PortType == AVAudioSession.PortBuiltInReceiver) {
                Log.LogInformation("EnsureCorrectOutputRoute: receiver detected, overriding to speaker");
                if (!session.OverrideOutputAudioPort(AVAudioSessionPortOverride.Speaker, out var error))
                    Log.LogWarning("EnsureCorrectOutputRoute: override failed: {Error}", error.LocalizedDescription);
                return;
            }
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
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
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
