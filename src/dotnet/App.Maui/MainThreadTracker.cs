namespace ActualChat.App.Maui;

public static class MainThreadTracker
{
    private static ILogger Log { get; } = StaticLog.For(typeof(MainThreadTracker));
    private static long _startTimestamp;
    private static long _invokeTimestamp;

    public static void Activate()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
        _ = Task.Run(DoWork);
    }

    private static async Task DoWork()
    {
        await Task.Yield();
        while (Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds < 30) {
            _invokeTimestamp = Stopwatch.GetTimestamp();
            Log.LogInformation("About to schedule MainThread tracker task");
            await MainThread.InvokeOnMainThreadAsync(RunOnMainThread).ConfigureAwait(false);
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static void RunOnMainThread()
    {
        var elapsed = Stopwatch.GetElapsedTime(_invokeTimestamp);
        var delay = (int)elapsed.TotalMilliseconds;
        var logLevel = delay >= 20 ? LogLevel.Warning : LogLevel.Information;
        Log.Log(logLevel,  "Running on MainThread within {Delay} ms after invocation", delay);
    }
}
