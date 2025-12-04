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

        WriteLine("created");
        _heartbeatTimer = new Timer(1000);
        _heartbeatTimer.Elapsed += (_, _) => WriteLine("alive");
        _heartbeatTimer.Start();
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        WriteLine("disposing");
        try {
            await base.DisposeAsync(disposing);
            if (disposing)
                _ = Services.Queues().Purge();
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
