using Android.OS;
using Android.Util;

namespace ActualChat.App.Maui;

/// <summary>
/// BlockCanary-style main-thread stall detector: times every main-looper message dispatch
/// via <see cref="Looper.SetMessageLogging"/> and warn-logs the ones exceeding the threshold,
/// including the dispatch target — the attribution Play Vitals ANR reports lack.
/// </summary>
public sealed class AndroidMainThreadMonitor : Java.Lang.Object, IPrinter
{
    private const int MaxMessageLength = 300;
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(1);

    private CpuTimestamp _startedAt;
    private string? _message;

    private static ILogger Log => field ??= StaticLog.For<AndroidMainThreadMonitor>();

    public static void Activate()
    {
        if (!MauiSettings.Diagnostics.EnableMainThreadMonitor)
            return;

        Looper.MainLooper!.SetMessageLogging(new AndroidMainThreadMonitor());
    }

    public void Println(string? message)
    {
        // Called on the main thread for every dispatched message: the ">>>>> Dispatching to ..."
        // branch must stay allocation-free; anything slow may run only on the rare slow-dispatch path.
        if (message.IsNullOrEmpty())
            return;

        if (message[0] == '>') {
            _startedAt = CpuTimestamp.Now;
            _message = message;
            return;
        }

        if (message[0] != '<' || _message is null)
            return;

        var elapsed = CpuTimestamp.Now - _startedAt;
        var dispatchMessage = _message;
        _message = null;
        if (elapsed < Threshold)
            return;

        if (dispatchMessage.Length > MaxMessageLength)
            dispatchMessage = dispatchMessage[..MaxMessageLength];
        Log.LogWarning("Main thread was blocked for {Elapsed} by: {Message}",
            elapsed.ToShortString(), dispatchMessage);
    }
}
