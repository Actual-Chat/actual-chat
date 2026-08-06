
namespace ActualChat.Hosting;

/// <summary>
/// Force-kill watchdog used by graceful-shutdown paths. Survives
/// threadpool starvation, hung finalizers, and stuck disposers — the
/// usual reasons <see cref="Environment.Exit"/> itself sometimes never
/// returns.
/// </summary>
public static class HardExit
{
    /// <summary>
    /// Schedules a hard process-exit after <paramref name="delay"/>,
    /// regardless of what graceful shutdown is doing. Idempotent — first
    /// caller wins, subsequent calls are no-ops so multiple stop
    /// triggers don't stack thread starts.
    /// </summary>
    public static void Schedule(TimeSpan delay, string reason)
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0)
            return;

        // Dedicated FOREGROUND thread (IsBackground=false) instead of
        // `Task.Run`. `Task.Run` lives on the threadpool, which can be
        // starved by exactly the kind of "graceful shutdown is hung"
        // scenario this watchdog is meant to recover from. A fresh
        // OS thread is scheduled even when the threadpool is wedged.
        var thread = new Thread(() => Run(delay, reason)) {
            IsBackground = false,
            Name = "HardExitWatchdog",
        };
        thread.Start();
    }

    // Private methods

    private static int _scheduled;

    private static void Run(TimeSpan delay, string reason)
    {
        Thread.Sleep(delay);
        Console.WriteLine($"HardExit: {reason} — hard-exiting.");

        // `FailFast` first. Unlike `Environment.Exit` it does NOT run
        // finalizers, AppDomain unload handlers, or
        // `IHostApplicationLifetime` stoppers — which is exactly what
        // we want, since those are what hung in the first place.
        try { Environment.FailFast($"HardExit: {reason}"); }
        catch { /* fall through */ }

        // Belt-and-suspenders. If `FailFast` somehow can't bring the
        // process down (rare, but seen on stuck Mono / odd hosts), kill
        // via the OS. `Process.Kill(true)` calls TerminateProcess on
        // Windows / SIGKILL on Linux — no finalizers, no chance to hang.
        try {
            using var self = Process.GetCurrentProcess();
            self.Kill(entireProcessTree: true);
        }
        catch { /* nothing left to try */ }

        // If we ever get here OS termination didn't take. Loop on
        // FailFast — the watchdog thread is foreground, so this keeps
        // the process visibly alive as a hint someone needs to look.
        for (;;) {
            Thread.Sleep(TimeSpan.FromSeconds(1));
            try { Environment.FailFast("HardExit: retry"); } catch { /* ignore */ }
        }
    }
}
