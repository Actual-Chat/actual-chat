using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Application = Android.App.Application;

namespace ActualChat.App.Maui;

public sealed class AndroidIncomingCallsBridge : IIncomingCallsBridge, IDisposable
{
    private readonly Lock _lock = new();
    private Ringtone? _ringtone;
    private Vibrator? _vibrator;

    private ILogger Log => field ??= StaticLog.For<AndroidIncomingCallsBridge>();
    private static Context Context => Application.Context;

    public void StartRinging()
    {
        lock (_lock) {
            try {
                var audioManager = (AudioManager?)Context.GetSystemService(Context.AudioService);
                var ringerMode = audioManager?.RingerMode ?? RingerMode.Normal;
                if (ringerMode != RingerMode.Silent)
                    StartVibration();
                if (ringerMode == RingerMode.Normal)
                    StartRingtone();
            }
            catch (Exception e) {
                Log.LogWarning(e, "StartRinging failed");
            }
        }
    }

    public void StopRinging()
    {
        lock (_lock) {
            try {
                _ringtone?.Stop();
                _ringtone = null;
                _vibrator?.Cancel();
                _vibrator = null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "StopRinging failed");
            }
        }
    }

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IncomingCallNotifications.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        => IncomingCallNotifications.Dismiss(chatId);

    public void Dispose()
        => StopRinging();

    // Private methods

    private void StartRingtone()
    {
        if (_ringtone is not null)
            return;

        var uri = RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);
        var ringtone = RingtoneManager.GetRingtone(Context, uri);
        if (ringtone is null)
            return;

        ringtone.AudioAttributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            ringtone.Looping = true;
        ringtone.Play();
        _ringtone = ringtone;
    }

    private void StartVibration()
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
