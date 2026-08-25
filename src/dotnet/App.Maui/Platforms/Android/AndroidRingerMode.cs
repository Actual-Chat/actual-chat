using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Media;
using Application = Android.App.Application;

namespace ActualChat.App.Maui;

// The single place that asks Android how loud this phone is allowed to be. Do Not Disturb needs
// its own read: bedtime mode leaves the ringer at Normal, so the ringer switch can't see it.
// Every probe fails open - one bad read must not silently kill PTT on this device.
public static class AndroidRingerMode
{
    private static ILogger Log => field ??= StaticLog.For(typeof(AndroidRingerMode));
    private static Context Context => Application.Context;

    public static DeviceRingerMode Mode {
        get {
            try {
                var audioManager = (AudioManager?)Context.GetSystemService(Context.AudioService);
                return audioManager?.RingerMode switch {
                    RingerMode.Silent => DeviceRingerMode.Silent,
                    RingerMode.Vibrate => DeviceRingerMode.Vibrate,
                    _ => DeviceRingerMode.Normal,
                };
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't read the ringer mode");
                return DeviceRingerMode.Normal;
            }
        }
    }

    public static bool IsDndActive {
        get {
            // Matched positively rather than as != All, so Unknown (and a missing manager) read
            // as "no DND" instead of silencing PTT on a device that simply couldn't answer.
            try {
                var notificationManager =
                    (NotificationManager?)Context.GetSystemService(Context.NotificationService);
                return notificationManager?.CurrentInterruptionFilter
                    is InterruptionFilter.Priority or InterruptionFilter.None or InterruptionFilter.Alarms;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't read the interruption filter");
                return false;
            }
        }
    }

    public static bool IsSilenced => Ptt.IsSilenced(Mode, IsDndActive);
}
