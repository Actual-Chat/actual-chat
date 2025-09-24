using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public static class IosAudioSessionHelper
{
    public static void ActivateRecordingAndBackgroundAudio()
        => BackgroundTask.Run(() => MainThread.InvokeOnMainThreadAsync(() => {
                var session = AVAudioSession.SharedInstance();

                session.SetCategory(
                    AVAudioSessionCategory.PlayAndRecord,
                    AVAudioSessionMode.VoiceChat,
                    AVAudioSessionCategoryOptions.AllowBluetooth
                    | AVAudioSessionCategoryOptions.AllowBluetoothA2DP
                    | AVAudioSessionCategoryOptions.DefaultToSpeaker
                    | AVAudioSessionCategoryOptions.OverrideMutedMicrophoneInterruption,
                    out NSError? categoryError
                );

                if (categoryError != null!)
                    throw StandardError.Internal(
                        $"Error setting audio category: {categoryError.LocalizedDescription}.");

                // Allow background audio
                session.SetActive(true,
                    AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation,
                    out NSError? setActiveError);

                if (setActiveError != null!)
                    throw StandardError.Internal(
                        $"Error activating audio session: {setActiveError.LocalizedDescription}.");
            }),
            StaticLog.For(typeof(IosAudioSessionHelper)),
            " Failed to activate audio session.");
}
