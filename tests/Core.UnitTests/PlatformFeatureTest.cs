using System.Diagnostics;
using ActualChat.Diagnostics;
using Stl.Time.Testing;
using Stl.Versioning.Providers;

namespace ActualChat.Core.UnitTests;

#pragma warning disable VSTHRD104

public class PlatformFeatureTest: TestBase
{
    private ServiceProvider Services { get; }

    public PlatformFeatureTest(ITestOutputHelper @out) : base(@out)
        => Services = new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton<IStateFactory>(c => new StateFactory(c))
            .AddSingleton(_ => LTagVersionGenerator.Default)
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
        var clock = new TestClock();
        var clockZero = clock.Now;
        var listener = new HealthEventListener(Services, 1);
        _ = BackgroundTask.Run(async () => {
            var process = Process.GetCurrentProcess();
            var processorCount = Environment.ProcessorCount;
            await foreach (var cCpuMean in listener.CpuMean.Changes()) {
                var cpuMean = cCpuMean.Value;
                var timeSpent = process.TotalProcessorTime;
                var timePassed = clock.Now - clockZero;
                var calcCpu = timeSpent / (processorCount * timePassed);
                Out.WriteLine("CPU Mean: {0}; TimeSpent: {1}; Calculated: {2};", cpuMean, timeSpent, calcCpu);
            }
        });

        _ = BackgroundTask.Run(async () => {
            await foreach (var cCpuMean in listener.CpuMean5.Changes()) {
                var cpuMean = cCpuMean.Value;
                Out.WriteLine("CPU Mean5: {0};", cpuMean);
            }
        });


        _ = BackgroundTask.Run(async () => {
            await foreach (var cCpuMean in listener.CpuMean20.Changes()) {
                var cpuMean = cCpuMean.Value;
                Out.WriteLine("CPU Mean20: {0};", cpuMean);
            }
        });

        // simulate CPU load
        var cycleCount = Math.Max(1, Environment.ProcessorCount / 4);
        for (int i = 0; i < cycleCount; i++) {
            _ = BackgroundTask.Run( async () => {
                for (long i = 0; i < long.MaxValue; i++) {
                    var x = Math.Sqrt(100d * i * i * i)*i / 239d;
                }
            });
        }

        await clock.Delay(5000);
        Out.WriteLine("CPU Count: "+ Environment.ProcessorCount);
    }
}
