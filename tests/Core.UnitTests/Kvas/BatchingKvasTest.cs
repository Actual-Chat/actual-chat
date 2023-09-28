using ActualChat.Core.UnitTests.Kvas.Services;
using ActualChat.Kvas;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat.Core.UnitTests.Kvas;

public class BatchingKvasTest : TestBase
{
    public BatchingKvasTest(ITestOutputHelper @out) : base(@out) { }

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddFusion();
        services.AddSingleton(_ => new TestBatchingKvasBackend() { Out = Out });
        services.AddSingleton(_ => new BatchingKvas.Options() {
            ReaderBatchSize = 10,
            ReaderWorkerPolicy = new BatchProcessorWorkerPolicy() { MaxWorkerCount = 1 },
            FlushDelay = TimeSpan.FromMilliseconds(10),
        });
        services.AddSingleton(c => new BatchingKvas(c.GetRequiredService<BatchingKvas.Options>(), c) {
            Backend = c.GetRequiredService<TestBatchingKvasBackend>(),
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task BasicTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<BatchingKvas>();

        await kvas.Set("a", "a");
        (await kvas.Get<string>("a")).Should().Be("a");
        await kvas.Set("b", 1);
        (await kvas.Get<int>("b")).Should().Be(1);
        await kvas.Set("c", "c");
        (await kvas.Get<string>("c")).Should().Be("c");
        await kvas.Set("d", "");
        (await kvas.Get<string>("d")).Should().Be("");
        await kvas.Set("d", (string?)null);
        (await kvas.Get<string?>("d")).Should().Be(null);

        await kvas.Remove("b");
        (await kvas.Get<int>("b")).Should().Be(0);
        (await kvas.TryGet<int>("b")).Should().Be(Option<int>.None);
        await kvas.Flush();

        var kvas2 = services.GetRequiredService<BatchingKvas>();
        var aTask = kvas2.Get<string>("a");
        var bTask = kvas2.Get<string>("b");
        var cTask = kvas2.Get<string>("c");
        var dTask = kvas2.Get<string>("a");
        (await aTask).Should().Be("a");
        (await bTask).Should().Be(null);
        (await cTask).Should().Be("c");
        (await dTask).Should().Be("a");

        var tasks = new List<Task<string?>>();
        for (var i = 0; i < 50; i++) {
            tasks.Add(kvas2.Get<string>("a").AsTask());
            PreciseDelay.Delay(TimeSpan.FromMilliseconds(1));
        }
        var results = await tasks.Collect();
        results.All(x => x == "a").Should().BeTrue();
    }

    [Fact]
    public async Task JsonHandlingTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<BatchingKvas>();

        var moment = CpuClock.Now;
        var buffer = new ArrayPoolBufferWriter<byte>();
        SystemJsonSerializer.Default.Write(buffer, moment);
        await kvas.Set("a", buffer.WrittenMemory.ToArray());
        (await kvas.Get<Moment>("a")).Should().Be(moment);
    }

    [Fact]
    public async Task StoredStateTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<BatchingKvas>();
        var prefixedKvas = kvas.WithPrefix(nameof(StoredStateTest));
        var stateFactory = services.StateFactory();

        // Instant set

        var s1 = stateFactory.NewKvasStored<string>(new(prefixedKvas, "s1"));
        s1.Value = "a";

        var s1a = stateFactory.NewKvasStored<string>(new(prefixedKvas, "s1"));
        await s1a.WhenRead;
        s1a.Value.Should().Be("a");

        // Delayed set

        var s2 = stateFactory.NewKvasStored<string>(new(prefixedKvas, "s2"));
        s2.Value = "b";

        await Task.Delay(400);
        kvas.ClearReadCache();

        var s2a = stateFactory.NewKvasStored<string>(new(prefixedKvas, "s2"));
        await s2a.WhenRead;
        s2a.Value.Should().Be("b");
        s2a.Value = "c";
        s2a.Value.Should().Be("c");
    }
}
