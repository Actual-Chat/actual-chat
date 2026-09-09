using Android.Content;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Formats the live routing facts a logcat reader needs next to a track or capture event:
/// the device Android actually routed to, and the platform state that decides such routing.
/// </summary>
public static class AndroidAudioRouteLog
{
    public static string Describe(AudioDeviceInfo? device)
        => device is null ? "none" : $"{device.Type}({device.ProductName})";

    public static string DescribeState()
    {
        try {
            var audioManager = (AudioManager)Platform.AppContext.GetSystemService(Context.AudioService)!;
            var commDevice = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? Describe(audioManager.CommunicationDevice)
                : "n/a";
            var musicVolume = DescribeVolume(audioManager, Android.Media.Stream.Music);
            var callVolume = DescribeVolume(audioManager, Android.Media.Stream.VoiceCall);
            return $"mode={audioManager.Mode}, scoOn={audioManager.BluetoothScoOn}, commDevice={commDevice}, "
                + $"musicVol={musicVolume}, callVol={callVolume}";
        }
        catch (Exception e) {
            return $"unavailable ({e.GetType().Name})";
        }
    }

    private static string DescribeVolume(AudioManager audioManager, Android.Media.Stream stream)
        => $"{audioManager.GetStreamVolume(stream)}/{audioManager.GetStreamMaxVolume(stream)}";
}
