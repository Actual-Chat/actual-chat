using ActualChat.Core.UnitTests.Kvas.Services;
using ActualChat.Kvas;
using MemoryPack;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat.Core.UnitTests.Kvas;

public class ComputedKvasTest(ITestOutputHelper @out) : TestBase(@out)
{
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
        using var buffer = new ArrayPoolBufferWriter<byte>();
        SystemJsonSerializer.Default.Write(buffer, moment);
        await kvas.Set("a", buffer.WrittenMemory.ToArray());
        (await kvas.Get<Moment>("a")).Should().Be(moment);
    }

    [Fact]
    public async Task SyncedStateTest1()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();
        var stateFactory = services.StateFactory();
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 1);
        var updateDelayer = FixedDelayer.MinDelay;

        // Instant set

        var s1 = stateFactory.NewKvasSynced<string>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        s1.Value = "a";
        await s1.WhenWritten().WaitAsync(timeout);

        var s2 = stateFactory.NewKvasSynced<string>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        await s2.WhenFirstTimeRead;
        s2.Value.Should().Be("a");

        s1.Value = "b";
        await s2.When(x => OrdinalEquals(x, "b")).WaitAsync(timeout);

        s2.Value = "c";
        await s1.When(x => OrdinalEquals(x, "c")).WaitAsync(timeout);

        s1.Value = "x1";
        s2.Value = "x2";
        await s1.WhenWritten().WaitAsync(timeout);
        await s2.WhenWritten().WaitAsync(timeout);
        await Task.Delay(TimeSpan.FromSeconds(1));
        s1.Value.Should().Be(s2.Value);
    }

    [Fact(Skip = "Flaky")]
    public async Task SyncedStateTest2()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();
        var stateFactory = services.StateFactory();
        var updateDelayer = FixedDelayer.MinDelay;
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 1);

        // Instant set

        var s1 = stateFactory.NewKvasSynced<StringState>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        s1.Value = "a";
        await Task.Delay(50); // NOTE(AY): Check why w/o this delay the test is failing on build server sometimes
        OrdinalEquals(s1.Value.Origin, s1.OwnOrigin).Should().BeTrue();
        await s1.WhenWritten().WaitAsync(timeout);

        var s2 = stateFactory.NewKvasSynced<StringState>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        await s2.WhenFirstTimeRead;
        s2.Value.Value.Should().Be("a");
        s2.Value.Origin.Should().Be(s1.OwnOrigin);

        s1.Value = "b";
        s1.Value.Origin.Should().Be(s1.OwnOrigin);
        await s2.When(x => OrdinalEquals(x.Value, "b")).WaitAsync(timeout);
        s2.Value.Origin.Should().Be(s1.OwnOrigin);

        s2.Value = "c";
        s2.Value.Origin.Should().Be(s2.OwnOrigin);
        await s1.When(x => OrdinalEquals(x.Value, "c")).WaitAsync(timeout);
        s1.Value.Origin.Should().Be(s2.OwnOrigin);

        s1.Value = "x1";
        s2.Value = "x2";
        await s1.WhenWritten().WaitAsync(timeout);
        await s2.WhenWritten().WaitAsync(timeout);
        await Task.Delay(timeout);
        s1.Value.Should().Be(s2.Value);
        s1.Value.Origin.Should().Be(s2.Value.Origin);
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record StringState(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Value,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Origin = ""
    ) : IHasOrigin
{
    public static implicit operator StringState(string value) => new (value);
}
