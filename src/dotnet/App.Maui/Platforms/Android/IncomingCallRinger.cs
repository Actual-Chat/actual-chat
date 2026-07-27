using Android.Content;
using Android.Media;
using Android.OS;
using Application = Android.App.Application;

namespace ActualChat.App.Maui;

// The single ring melody/vibration source, driven by IncomingCallUI via AndroidIncomingCallsBridge
// in every case (foreground and over the lock screen). Uses a looping MediaPlayer rather than
// Ringtone: Ringtone.Play() is unreliable on the first invocation (occasionally silent), while a
// prepared MediaPlayer plays deterministically.
public static class IncomingCallRinger
{
    private static readonly Lock Lock = new();
    private static MediaPlayer? _player;
    private static Vibrator? _vibrator;
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.For(typeof(IncomingCallRinger));
    private static Context Context => Application.Context;

    public static bool IsPlaying {
        get {
            lock (Lock)
                return _player is not null;
        }
    }

    public static void Start()
    {
        lock (Lock) {
            try {
                var audioManager = (AudioManager?)Context.GetSystemService(Context.AudioService);
                var ringerMode = audioManager?.RingerMode ?? RingerMode.Normal;
                if (ringerMode != RingerMode.Silent)
                    StartVibration();
                if (ringerMode == RingerMode.Normal)
                    StartRingtone();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Start failed");
            }
        }
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
