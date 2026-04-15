using Android.Media;

namespace ActualChat.App.Maui.Audio;

// NOTE: Must be a top-level class. The Xamarin.Android ACW generator failed to emit a
// Java wrapper for this listener when it was a nested class inside AndroidAudioFocusHelper
// (both doubly- and singly-nested variants were tried), causing ClassNotFoundException
// at runtime when the instance was passed to AudioManager.AddOnCommunicationDeviceChangedListener.
internal sealed class CommunicationDeviceListener(AudioManager audioManager, ILogger log) : Java.Lang.Object,
    AudioManager.IOnCommunicationDeviceChangedListener
{
    public void OnCommunicationDeviceChanged(AudioDeviceInfo? device)
        => log.LogInformation(
            "Communication device changed callback: {Type}, current: {Current}",
            device?.Type, audioManager.CommunicationDevice?.Type);
}
