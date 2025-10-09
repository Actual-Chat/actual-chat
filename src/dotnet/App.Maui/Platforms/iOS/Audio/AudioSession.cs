using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class AudioSession(AppUIHub hub) : IAsyncDisposable
{
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());

    public ValueTask DisposeAsync()
        => BackgroundTask.Run(() => MainThread.InvokeOnMainThreadAsync(() => {
                    var session = AVAudioSession.SharedInstance();
                    session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation)
                        .Assert("Failed to deactivate session");
                }),
                Log,
                "Failed to dispose AudioSession")
            .ToValueTask();

    public Task Reconfigure(AudioMode mode)
        => MainThread.InvokeOnMainThreadAsync(() => ReconfigureUnsafe(mode));

    private void ReconfigureUnsafe(AudioMode minMode)
    {
        var session = AVAudioSession.SharedInstance();
        session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation).Assert("Failed to deactivate session");
        ConfigureUnsafe(session, minMode);
        session.SetActive(true).Assert("Failed to activate session");
    }

    private void ConfigureUnsafe(AVAudioSession session, AudioMode mode)
    {
        if (mode is AudioMode.Recording) {
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                    AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetooth)
                .Assert($"{mode}: failed to set category");
            session.SetPreferredIOBufferDuration(Constants.Audio.OpusFrameDuration.TotalSeconds, out var error);
            error.Assert("Failed to set preferred IO buffer duration");
        }
        else if (mode is AudioMode.Playback)
            session.SetCategory(AVAudioSessionCategory.Playback).Assert($"{mode}: failed to set category");
        else
            session.SetCategory(AVAudioSessionCategory.Ambient).Assert($"{mode}: failed to set category");
    }
}
