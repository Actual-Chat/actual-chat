using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Application = Android.App.Application;

namespace ActualChat.App.Maui;

// The single ring melody/vibration source, driven by IncomingCallUI via AndroidIncomingCallsBridge
// once Blazor is up, and started natively together with a shown call notification. Uses a looping
// MediaPlayer rather than Ringtone: Ringtone.Play() is unreliable on the first invocation
// (occasionally silent), while a prepared MediaPlayer plays deterministically.
public static class IncomingCallRinger
{
    private static readonly Lock Lock = new();
    private static MediaPlayer? _player;
    private static Vibrator? _vibrator;
    private static ILogger? _log;
    private static int _generation;

    private static ILogger Log => _log ??= StaticLog.For(typeof(IncomingCallRinger));
    private static Context Context => Application.Context;

    public static bool IsPlaying {
        get {
            lock (Lock)
                return _player is not null;
        }
    }

    public static void Start(TimeSpan? autoStopAfter = null)
    {
        int generation;
        lock (Lock) {
            generation = ++_generation;
            try {
                // DND is deliberately not consulted: an incoming call is the user's own contact
                // reaching them, not a background alert.
                var ringerMode = AndroidRingerMode.Mode;
                if (ringerMode != DeviceRingerMode.Silent)
                    StartVibration();
                if (ringerMode == DeviceRingerMode.Normal)
                    StartRingtone();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Start failed");
            }
        }
        if (autoStopAfter is { } delay)
            _ = StopAfter(delay, generation);
    }

    public static void Stop()
    {
        lock (Lock) {
            // Released independently: a throwing player must not leave the vibrator buzzing forever.
            var player = _player;
            _player = null;
            var vibrator = _vibrator;
            _vibrator = null;
            try {
                player?.Release(); // Valid from any state, incl. Error - no preceding Stop needed
            }
            catch (Exception e) {
                Log.LogWarning(e, "Stop: player release failed");
            }
            try {
                vibrator?.Cancel();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Stop: vibrator cancel failed");
            }
        }
    }

    // Private methods

    private static async Task StopAfter(TimeSpan delay, int generation)
    {
        // Only the start that armed this may be stopped by it: any later Start or Stop moved the generation on.
        await Task.Delay(delay).ConfigureAwait(false);
        bool isCurrent;
        lock (Lock)
            isCurrent = _generation == generation;
        if (isCurrent)
            Stop();
    }

    private static void StartRingtone()
    {
        if (_player is not null)
            return;

        var uri = IncomingCallNotifications.RingtoneUri;
        if (uri is null) {
            Log.LogWarning("Ringer: no ringtone uri (default ringtone is 'None'?)");
            return;
        }

        var player = new MediaPlayer();
        player.SetDataSource(Context, uri);
        player.SetAudioAttributes(new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Music)!
            .Build()!);
        player.Looping = true;
        player.Prepare();
        player.Start();
        _player = player;
    }

    private static void StartVibration()
    {
        if (_vibrator is not null)
            return;

        var vibrator = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? ((VibratorManager?)Context.GetSystemService(Context.VibratorManagerService))?.DefaultVibrator
            : (Vibrator?)Context.GetSystemService(Context.VibratorService);
        if (vibrator is null || !vibrator.HasVibrator)
            return;

        var effect = VibrationEffect.CreateWaveform([0, 700, 500, 700, 500, 500], 0);
        vibrator.Vibrate(effect);
        _vibrator = vibrator;
    }
}
