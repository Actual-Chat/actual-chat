using ActualChat.App.Server;
using Xunit.DependencyInjection;
using Timer = System.Timers.Timer;

namespace ActualChat.Testing.Host;

public class TestAppHost : AppHost
{
    private static long _lastId;

    private readonly Timer _heartbeatTimer;

    public TestAppHostOptions Options { get; }
    public long Id { get; }
    public CpuTimestamp StartedAt { get; } = CpuTimestamp.Now;
    public TestOutputHelperAccessor OutputAccessor { get; }
    public ITestOutputHelper? Output { get => OutputAccessor.Output; set => OutputAccessor.Output = value; }

    public TestAppHost(TestAppHostOptions options, TestOutputHelperAccessor outputAccessor)
    {
        Options = options;
        OutputAccessor = outputAccessor;
        Id = Interlocked.Increment(ref _lastId);
        IsTestHost = true;

        WriteLine("created");
        _heartbeatTimer = new Timer(1000);
        _heartbeatTimer.Elapsed += (_, _) => WriteLine("alive");
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
                await Services.Queues().Purge();
            await base.DisposeAsync(disposing);
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
}
