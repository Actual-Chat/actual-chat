using Android.Bluetooth;

namespace ActualChat.App.Maui.Audio;

// A top-level class for the same reason as CommunicationDeviceListener: the ACW generator
// skipped a nested listener passed to the platform, which surfaced as ClassNotFoundException.
internal sealed class HeadsetProfileListener(Action<BluetoothHeadset?> onConnected) : Java.Lang.Object,
    IBluetoothProfileServiceListener
{
    public void OnServiceConnected(ProfileType profile, IBluetoothProfile? proxy)
        => onConnected(proxy as BluetoothHeadset);

    public void OnServiceDisconnected(ProfileType profile)
        => onConnected(null);
}
