using Android.Media;

namespace ActualChat.App.Maui.Audio;

// NOTE: Must be a top-level class. The Xamarin.Android ACW generator failed to emit a
// Java wrapper for this listener when it was a nested class inside AndroidAudioFocusHelper
// (both doubly- and singly-nested variants were tried), causing ClassNotFoundException
// at runtime when the instance was passed to AudioManager.AddOnCommunicationDeviceChangedListener.
internal sealed class CommunicationDeviceListener(ILogger log) : Java.Lang.Object,
    AudioManager.IOnCommunicationDeviceChangedListener
{
    private static readonly Lock Lock = new();
    private static AudioDeviceInfo? _lastDevice;
    private static bool _isHeard;

    // Read this instead of AudioManager.CommunicationDevice: while a route change is landing, that one
    // blocks in AudioService for 3s - and it was read on every playback start, under the focus lock.
    public static AudioDeviceInfo? LastDevice => Volatile.Read(ref _lastDevice);

    public static Task Seed(AudioManager audioManager, ILogger log)
        // Off the caller's thread: that read can block for 3s too, and the caller runs at startup,
        // which an incoming call may be launching. A device the callback reports meanwhile is newer.
        => BackgroundTask.Run(() => {
            var device = audioManager.CommunicationDevice;
            lock (Lock)
                if (!_isHeard)
                    Volatile.Write(ref _lastDevice, device);
            return Task.CompletedTask;
        }, log, "Failed to read the communication device");

    public void OnCommunicationDeviceChanged(AudioDeviceInfo? device)
    {
        // Never read AudioManager.CommunicationDevice here: it's a blocking binder call back into
        // AudioService from its own main-thread callback, and it stalled there for 3s.
        lock (Lock) {
            _isHeard = true;
            Volatile.Write(ref _lastDevice, device);
        }
        log.LogInformation("Communication device changed callback: {Type}", device?.Type);
    }
}
