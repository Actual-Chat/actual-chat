using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas;

public class KvasTest : TestBase
{
    public KvasTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        var kvasBackend = new TestKvasBackend() { Out = Out };
        var options = new KvasForBackend.Options() {
            ReadBatchConcurrencyLevel = 1,
            ReadBatchDelayTaskFactory = null,
            ReadBatchMaxSize = 10,
            FlushDelay = TimeSpan.FromMilliseconds(10),
        };
        var kvas = new KvasForBackend(options, kvasBackend);

        await kvas.Set("a", "a");
        (await kvas.Get("a")).Should().Be("a");
        await kvas.Set("b", "b");
        (await kvas.Get("b")).Should().Be("b");
        await kvas.Set("c", "c");
        (await kvas.Get("c")).Should().Be("c");
        await kvas.Remove("b");
        (await kvas.Get("b")).Should().Be(null);
        await kvas.Flush();

        var kvas2 = new KvasForBackend(options, kvasBackend);
        var aTask = kvas2.Get("a");
        var bTask = kvas2.Get("b");
        var cTask = kvas2.Get("c");
        var dTask = kvas2.Get("a");
        (await aTask).Should().Be("a");
        (await bTask).Should().Be(null);
        (await cTask).Should().Be("c");
        (await dTask).Should().Be("a");

        var tasks = new List<Task<string?>>();
        for (var i = 0; i < 50; i++) {
            tasks.Add(kvas2.Get("a").AsTask());
            PreciseDelay.Delay(TimeSpan.FromMilliseconds(1));
        }
        var results = await tasks.Collect();
        results.All(x => x == "a").Should().BeTrue();
    }

    [Fact]
    public async Task StoredStateTest()
    {
        var kvasBackend = new TestKvasBackend() { Out = Out };
        var options = new KvasForBackend.Options() {
            ReadBatchConcurrencyLevel = 1,
            ReadBatchDelayTaskFactory = null,
            ReadBatchMaxSize = 10,
            FlushDelay = TimeSpan.FromMilliseconds(10),
        };
        var kvasForBackend = new KvasForBackend(options, kvasBackend);

        await using var services = new ServiceCollection()
            .AddFusion().Services
            .AddSingleton<IKvas>(_ => kvasForBackend)
            .AddSingleton(typeof(IKvas<>), typeof(KvasForScope<>))
            .BuildServiceProvider();
        var stateFactory = services.StateFactory();

        // Instant set

        var s1 = stateFactory.NewStored<string>(GetType(), "s1");
        s1.Value = "a";

        await Task.Delay(20);

        var s1a = stateFactory.NewStored<string>(GetType(), "s1");
        s1a.Value.Should().BeNull();
        await Task.Delay(20);
        s1a.Value.Should().Be("a");

        // Delayed set

        var s2 = stateFactory.NewStored<string>(GetType(), "s2");
        await Task.Delay(20);
        s2.Value = "b";

        await Task.Delay(20);

        kvasForBackend.ClearReadCache();
        var s2a = stateFactory.NewStored<string>(GetType(), "s2");
        s2a.Value.Should().BeNull();
        await Task.Delay(20);
        s2a.Value.Should().Be("b");
        s2a.Value = "c";
        s2a.Value.Should().Be("c");
    }

    [Fact]
    public async Task PrimarySecondaryTest()
    {
        var kvasBackend1 = new TestKvasBackend() { Out = Out };
        var kvasBackend2 = new TestKvasBackend() { Out = Out };
        var options = new KvasForBackend.Options() {
            ReadBatchConcurrencyLevel = 1,
            ReadBatchDelayTaskFactory = null,
            ReadBatchMaxSize = 10,
            FlushDelay = TimeSpan.FromMilliseconds(1000),
        };
        var kvas1 = new KvasForBackend(options, kvasBackend1);
        var kvas2 = new KvasForBackend(options, kvasBackend2);
        var kvas = kvas1.WithSecondary(kvas2);

        await kvas.Set("a", "a");
        (await kvas1.Get("a")).Should().Be("a");
        (await kvas2.Get("a")).Should().Be("a");
        (await kvas.Get("a")).Should().Be("a");

        kvas1.ClearReadCache();
        kvas2.ClearReadCache();
        (await kvas1.Get("a")).Should().Be(null);
        (await kvas2.Get("a")).Should().Be(null);
        (await kvas.Get("a")).Should().Be(null);

        await kvas.Flush();
        (await kvas1.Get("a")).Should().Be("a");
        (await kvas2.Get("a")).Should().Be("a");

        await kvas1.Remove("a");
        (await kvas1.Get("a")).Should().Be(null);
        (await kvas.Get("a")).Should().Be("a");

        await kvas.Flush();
        kvas1.ClearReadCache();
        kvas2.ClearReadCache();
        (await kvas1.Get("a")).Should().Be(null);
        (await kvas.Get("a")).Should().Be("a");
    }
}
