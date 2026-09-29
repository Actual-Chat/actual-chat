using ActualChat.App.Server;
using Timer = System.Timers.Timer;

namespace ActualChat.Testing.Host;

public class TestAppHost : AppHost
{
    private static long _lastId;

    private readonly Timer _heartbeatTimer;
    private readonly Lock _heartbeatLock = new();
    private CpuTimestamp _lastHeartbeatAt;
    private TimeSpan _lastCpuTime;
    private TimeSpan _lastGCPauseDuration;
    private MachineStat _lastMachineStat;

    public TestAppHostOptions Options { get; }
    public long Id { get; }
    public CpuTimestamp StartedAt { get; } = CpuTimestamp.Now;
    public TestOutputAccessor OutputAccessor { get; }
    public ITestOutputHelper? Output { get => OutputAccessor.Output; set => OutputAccessor.Output = value; }

    public TestAppHost(TestAppHostOptions options, TestOutputAccessor outputAccessor)
    {
        Options = options;
        OutputAccessor = outputAccessor;
        Id = Interlocked.Increment(ref _lastId);
        IsTestHost = true;

        WriteLine("created");
        _lastHeartbeatAt = StartedAt;
        _lastCpuTime = Environment.CpuUsage.TotalTime;
        _lastGCPauseDuration = GC.GetTotalPauseDuration();
        _lastMachineStat = ReadMachineStat();
        _heartbeatTimer = new Timer(1000);
        _heartbeatTimer.Elapsed += (_, _) => WriteLine($"alive: {GetLoadInfo()}");
        _heartbeatTimer.Start();
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        WriteLine("disposing");
        try {
            // Purge BEFORE base.DisposeAsync — queue processors must still be alive
            // to perform the purge. And AWAIT it: leftover fire-and-forget purges from
            // a finished test could race with the next test's NewAppHost and drain
            // the new host's in-flight FlowResumeEvents (same-class tests share a
            // NATS stream via the stable CoreSettings.Instance prefix).
            if (disposing)
                await Services.Queues().PurgeWithTimeout(TimeSpan.FromSeconds(10), WriteLine);
            // Bound base dispose: StopAsync on IHost can hang if a shard/worker ignores
            // its stop token. Without this, a wedged background task makes `await using var h`
            // never return and the test appears silent until the outer blame-hang-timeout.
            await base.DisposeAsync(disposing).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException) {
            WriteLine("base.DisposeAsync TIMED OUT after 20s — abandoning");
        }
        catch (Exception) {
            // Intended
        }
        _heartbeatTimer.Stop();
        _heartbeatTimer.Dispose();
        WriteLine("disposed");
    }

    public void WriteLine(string message)
        => Output?.WriteLine(
            $"<{StartedAt.Elapsed.ToShortString()}> AppHost[{Id}, '{Options.InstanceName}']: {message}");

    // Private methods

    // A late tick means the thread pool was slow to run the timer; an on-time one says nothing about
    // the CPU, which is what the process and machine CPU figures are for. Output is shown on failure only.
    private string GetLoadInfo()
    {
        lock (_heartbeatLock) {
            var now = CpuTimestamp.Now;
            var cpuTime = Environment.CpuUsage.TotalTime;
            var gcPauseDuration = GC.GetTotalPauseDuration();
            var machineStat = ReadMachineStat();
            var interval = now - _lastHeartbeatAt;
            var cpuCoreCount = (cpuTime - _lastCpuTime).TotalSeconds / interval.TotalSeconds;
            var info = $"tick {interval.ToShortString()}, "
                + $"cpu {cpuCoreCount:F1} of {Environment.ProcessorCount} cores, "
                + $"pool {ThreadPool.ThreadCount} threads {ThreadPool.PendingWorkItemCount} queued, "
                + $"gc pause +{(gcPauseDuration - _lastGCPauseDuration).ToShortString()}"
                + FormatMachineLoad(_lastMachineStat, machineStat);
            _lastHeartbeatAt = now;
            _lastCpuTime = cpuTime;
            _lastGCPauseDuration = gcPauseDuration;
            _lastMachineStat = machineStat;
            return info;
        }
    }

    // Linux only, which is what CI runs on; elsewhere the machine figures are just left out
    private static MachineStat ReadMachineStat()
    {
        if (!OperatingSystem.IsLinux())
            return default;

        try {
            var lines = File.ReadAllLines("/proc/stat");
            // "cpu user nice system idle iowait irq softirq steal ..." - idle and iowait are the idle time
            var ticks = lines[0]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Take(8).Select(long.Parse).ToArray();
            var total = ticks.Sum();
            return new MachineStat(
                total - ticks[3] - ticks[4], ticks[4], total,
                ReadCounter(lines, "procs_running"), ReadCounter(lines, "procs_blocked"));
        }
        catch (IOException) {
            return default;
        }

        static int ReadCounter(string[] lines, string name) {
            var line = lines.FirstOrDefault(x => x.StartsWith(name + " "));
            return line is null ? -1 : int.Parse(line.AsSpan(name.Length + 1));
        }
    }

    private static string FormatMachineLoad(MachineStat last, MachineStat current)
    {
        // loadavg alone can't tell a CPU queue from an I/O wait: it counts both, over a minute.
        // The run/io-blocked thread counts are instant, iowait is the share of the last interval.
        if (current.Total <= last.Total)
            return "";

        var totalDelta = (double)(current.Total - last.Total);
        var busyPercent = 100 * (current.Busy - last.Busy) / totalDelta;
        var ioWaitPercent = 100 * (current.IoWait - last.IoWait) / totalDelta;
        return $", machine cpu {busyPercent:F0}% iowait {ioWaitPercent:F0}%, "
            + $"run {current.RunningCount} io-blocked {current.BlockedCount}, loadavg {ReadLoadAverage()}";
    }

    private static string ReadLoadAverage()
    {
        try {
            return File.ReadAllText("/proc/loadavg").Split(' ', 2)[0];
        }
        catch (IOException) {
            return "?";
        }
    }

    // Nested types

    private readonly record struct MachineStat(
        long Busy,
        long IoWait,
        long Total,
        int RunningCount,
        int BlockedCount);
}
