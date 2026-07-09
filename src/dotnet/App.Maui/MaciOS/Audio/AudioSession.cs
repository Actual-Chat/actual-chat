using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class AudioSession(AppUIHub hub) : IAsyncDisposable
{
    private ILogger Log => field ??= hub.LogFor(GetType());

    public ValueTask DisposeAsync()
        => BackgroundTask.Run(() => DispatchToMainThread(() => {
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

    public AudioSessionDiagnostics GetDiagnostics()
    {
        var session = AVAudioSession.SharedInstance();
        var outputs = session.CurrentRoute?.Outputs ?? [];
        var routes = new List<string>(outputs.Length);
        foreach (var output in outputs)
            routes.Add($"{output.PortName} ({output.PortType})");
        return new AudioSessionDiagnostics(
            session.Category ?? "",
            session.Mode ?? "",
            session.OtherAudioPlaying,
            routes);
    }

    private void ReactivateUnsafe(AudioFocusMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        ConfigureUnsafe(session, mode);

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
