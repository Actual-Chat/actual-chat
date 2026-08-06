using ActualChat.Diagnostics;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests;

#pragma warning disable VSTHRD104

public class PlatformFeatureTest(ITestOutputHelper @out) : TestBase(@out)
{
    private ServiceProvider Services { get; } = new ServiceCollection()
        .AddFusion()
        .Services
        .BuildServiceProvider();

    [Fact]
    public void DefaultValueTaskTest()
    {
        var vt = default(ValueTask<string?>);
        vt.IsCompleted.Should().BeTrue();
        vt.Result.Should().BeNull();
    }

    [Fact]
    public async Task HealthEventListenerTest()
    {
        using var clock = new TestClock();
        var clockZero = clock.Now;
        using var listener = new HealthEventListener(Services, 1);
        // ReSharper disable AccessToDisposedClosure
        _ = BackgroundTask.Run(async () => {
            var process = Process.GetCurrentProcess();
            var processorCount = Environment.ProcessorCount;
            await foreach (var cCpuMean in listener.CpuMean.Computed.Changes()) {
                var cpuMean = cCpuMean.Value;
                var timeSpent = process.TotalProcessorTime;
                var timePassed = clock.Now - clockZero;
                var calcCpu = timeSpent / (processorCount * timePassed);
                WriteLine($"CPU Mean: {cpuMean}; TimeSpent: {timeSpent}; Calculated: {calcCpu};");
            }
        });

        _ = BackgroundTask.Run(async () => {
            await foreach (var cCpuMean in listener.CpuMean5.Computed.Changes()) {
                var cpuMean = cCpuMean.Value;
                WriteLine($"CPU Mean5: {cpuMean};");
            }
        });


        _ = BackgroundTask.Run(async () => {
            await foreach (var cCpuMean in listener.CpuMean20.Computed.Changes()) {
                var cpuMean = cCpuMean.Value;
                WriteLine($"CPU Mean20: {cpuMean};");
            }
        });

        // simulate CPU load
        var cycleCount = Math.Max(1, Environment.ProcessorCount / 4);
        for (int i = 0; i < cycleCount; i++) {
            _ = BackgroundTask.Run(() => {
                for (long l = 0; l < long.MaxValue; l++)
                    _ = Math.Sqrt(100d * l * l * l)*l / 239d;
                return Task.CompletedTask;
            });
        }
        // ReSharper restore AccessToDisposedClosure
        await clock.Delay(5000);
        WriteLine("CPU Count: "+ Environment.ProcessorCount);
    }
}
