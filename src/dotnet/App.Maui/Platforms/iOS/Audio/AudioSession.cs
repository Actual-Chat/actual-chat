using ActualChat.Maui;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

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

    private void ReactivateUnsafe(AudioFocusMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        ConfigureUnsafe(session, mode);

        if (!session.SetActive(true, out var error)) {
            Log.LogWarning("Failed to re-activate audio session: {Error}", error.LocalizedDescription);
            // Deactivate and retry
            session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
            session.SetActive(true, out error);
            error.Assert("Failed to re-activate audio session after retry");
        }
    }

    private void ReconfigureUnsafe(AudioFocusMode minMode)
    {
        var session = AVAudioSession.SharedInstance();
        session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation).Assert("Failed to deactivate session");
        ConfigureUnsafe(session, minMode);
        session.SetActive(true).Assert("Failed to activate session");
    }

    private void ConfigureUnsafe(AVAudioSession session, AudioFocusMode mode)
    {
        if (mode is AudioFocusMode.Recording) {
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                    AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetooth)
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
