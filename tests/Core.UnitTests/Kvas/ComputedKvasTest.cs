using ActualChat.Core.UnitTests.Kvas.Services;
using ActualChat.Kvas;
using ActualLab.Generators;
using CommunityToolkit.HighPerformance.Buffers;
using MemoryPack;

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

        var moment = CpuClock.Instance.Now;
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
        var updateDelayer = FixedDelayer.NextTick;

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
        await s2.Computed.When(x => OrdinalEquals(x, "b")).WaitAsync(timeout);

        s2.Value = "c";
        await s1.Computed.When(x => OrdinalEquals(x, "c")).WaitAsync(timeout);

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
        var updateDelayer = FixedDelayer.NextTick;
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
        await s2.Computed.When(x => OrdinalEquals(x.Value, "b")).WaitAsync(timeout);
        s2.Value.Origin.Should().Be(s1.OwnOrigin);

        s2.Value = "c";
        s2.Value.Origin.Should().Be(s2.OwnOrigin);
        await s1.Computed.When(x => OrdinalEquals(x.Value, "c")).WaitAsync(timeout);
        s1.Value.Origin.Should().Be(s2.OwnOrigin);

        s1.Value = "x1";
        s2.Value = "x2";
        await s1.WhenWritten().WaitAsync(timeout);
        await s2.WhenWritten().WaitAsync(timeout);
        await Task.Delay(timeout);
        s1.Value.Should().Be(s2.Value);
        s1.Value.Origin.Should().Be(s2.Value.Origin);
    }

    [Theory] // TODO(AY): sometimes fails
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(11, true)]
    [InlineData(11, false)]
    [InlineData(111, true)]
    [InlineData(111, false)]
    [InlineData(1111, true)]
    [InlineData(1111, false)]
    public async Task WhenShouldWaitForValue(int propertyCount, bool mustUpdateBeforeWaiting)
    {
        var rsg = new RandomStringGenerator(alphabet: Alphabet.AlphaNumericLower);
        var keyPrefix = rsg.Next(5);
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();
        var stateFactory = services.StateFactory();
        var updateDelayer = FixedDelayer.NextTick;
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 5);
        for (var i = 0; i < propertyCount; i++) {
            using var cts = new CancellationTokenSource(timeout);
            var cancellationToken = cts.Token;
            using var state = stateFactory.NewKvasSynced<bool>(new (kvas, $"{keyPrefix}.b{i}") {
                InitialValue = false,
                UpdateDelayer = updateDelayer,
            });
            state.Value = true;
            if (mustUpdateBeforeWaiting)
                await state.Update(cancellationToken);
            await state.Computed.When(x => x, cancellationToken);
        }
    }

    [Fact] // TODO(AY): sometimes fails
    public async Task ValueOrDefaultMustReturnNewValueAfterUpdate()
    {
        // arrange
        var rsg = new RandomStringGenerator(alphabet: Alphabet.AlphaNumericLower);
        var services = CreateServices();
        var kvas = services.GetRequiredService<IKvas>();
        var stateFactory = services.StateFactory();
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 1);
        var updateDelayer = FixedDelayer.NextTick;
        using var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;
        using var state = stateFactory.NewKvasSynced<bool>(new(kvas, $"{rsg.Next(5)}.b1") {
            UpdateDelayer = updateDelayer,
        });

        // act
        state.Value = true;
        await state.Computed.When(x => x, cancellationToken);

        // assert
        await TestExt.When(() => state.ValueOrDefault.Should().BeTrue(), TimeSpan.FromSeconds(10));
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
