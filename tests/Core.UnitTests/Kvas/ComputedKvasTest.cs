using ActualChat.Core.UnitTests.Kvas.Services;
using ActualChat.Kvas;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat.Core.UnitTests.Kvas;

public class ComputedKvasTest : TestBase
{
    public ComputedKvasTest(ITestOutputHelper @out) : base(@out) { }

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        var fusion = services.AddFusion();
        fusion.AddService<IKvas, TestComputedKvas>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task BasicTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();

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

        var kvas2 = services.GetRequiredService<IKvas>();
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
        var kvas = services.GetRequiredService<IKvas>();

        var moment = CpuClock.Now;
        var buffer = new ArrayPoolBufferWriter<byte>();
        SystemJsonSerializer.Default.Write(buffer, moment);
        await kvas.Set("a", buffer.WrittenMemory.ToArray());
        (await kvas.Get<Moment>("a")).Should().Be(moment);
    }

    [Fact]
    public async Task SyncedStateTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();
        var stateFactory = services.StateFactory();
        var timeout = TimeSpan.FromSeconds(1);

        // Instant set

        var s1 = stateFactory.NewKvasSynced<string>(new(kvas, "s1"));
        s1.Value = "a";
        await s1.WhenWritten().WaitAsync(timeout);

        var s1a = stateFactory.NewKvasSynced<string>(new(kvas, "s1"));
        await s1a.WhenFirstTimeRead;
        s1a.Value.Should().Be("a");

        s1.Value = "b";
        await s1a.When(x => OrdinalEquals(x, "b")).WaitAsync(timeout);
    }
}
