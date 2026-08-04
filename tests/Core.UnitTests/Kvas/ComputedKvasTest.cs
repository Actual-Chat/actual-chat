using ActualChat.Core.UnitTests.Kvas.Services;
using ActualChat.Kvas;
using ActualLab.Generators;
using CommunityToolkit.HighPerformance.Buffers;

namespace ActualChat.Core.UnitTests.Kvas;

public class ComputedKvasTest(ITestOutputHelper @out) : TestBase(@out)
{
    private IServiceProvider CreateServices(Func<IServiceCollection, IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddXUnit(Out));
        var fusion = services.AddFusion();
        fusion.AddService<TestComputedKvas>();
        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [FlakyFact("AY: Time-dependent", 3)]
    public async Task BasicTest()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<TestComputedKvas>();

        await kvas.Set("a", "a");
        (await kvas.Get<string>("a")).Should().Be("a");
        await kvas.Set("b", Box.New(1));
        (await kvas.Get<Box<int>>("b"))!.Value.Should().Be(1);
        await kvas.Set("c", "c");
        (await kvas.Get<string>("c")).Should().Be("c");
        await kvas.Set("d", "");
        (await kvas.Get<string>("d")).Should().Be("");
        await kvas.Set("d", (string?)null);
        (await kvas.Get<string>("d")).Should().Be(null);

        await kvas.Set("b", null);
        (await kvas.Get<Box<int>>("b")).Should().BeNull();

        var kvas2 = services.GetRequiredService<TestComputedKvas>();
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
        var kvas = services.GetRequiredService<TestComputedKvas>();

        var value = "test-value";
        using var buffer = new ArrayPoolBufferWriter<byte>();
        Serializers.SystemJson.Write(buffer, value, typeof(string));
        await kvas.Set("a", buffer.WrittenMemory.ToArray());
        (await kvas.Get<string>("a")).Should().Be(value);
    }

    [FlakyFact("AY: Time-dependent", 3)]
    public async Task SyncedStateTest1()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<TestComputedKvas>();
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
        await s2.Computed.When(x => x == "b").WaitAsync(timeout);

        s2.Value = "c";
        await s1.Computed.When(x => x == "c").WaitAsync(timeout);

        s1.Value = "x1";
        s2.Value = "x2";
        await s1.WhenWritten().WaitAsync(timeout);
        await s2.WhenWritten().WaitAsync(timeout);
        await Task.Delay(TimeSpan.FromSeconds(1));
        s1.Value.Should().Be(s2.Value);
    }

    [FlakyFact("AY: Time-dependent", 3)]
    public async Task SyncedStateTest2()
    {
        var services = CreateServices();
        var kvas = services.GetRequiredService<TestComputedKvas>();
        var stateFactory = services.StateFactory();
        var updateDelayer = FixedDelayer.NextTick;
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 1);

        // Instant set

        var s1 = stateFactory.NewKvasSynced<StringState>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        s1.Value = "a";
        (s1.Value.Origin == s1.OwnOrigin).Should().BeTrue();
        await Task.Delay(100);
        (s1.Value.Origin == s1.OwnOrigin).Should().BeTrue();
        await s1.WhenWritten().WaitAsync(timeout);

        var s2 = stateFactory.NewKvasSynced<StringState>(new(kvas, "s1") {
            UpdateDelayer = updateDelayer,
        });
        await s2.WhenFirstTimeRead;
        s2.Value.Value.Should().Be("a");
        s2.Value.Origin.Should().Be(s1.OwnOrigin);

        s1.Value = "b";
        s1.Value.Origin.Should().Be(s1.OwnOrigin);
        await s2.Computed.When(x => x.Value == "b").WaitAsync(timeout);
        s2.Value.Origin.Should().Be(s1.OwnOrigin);

        s2.Value = "c";
        s2.Value.Origin.Should().Be(s2.OwnOrigin);
        await s1.Computed.When(x => x.Value == "c").WaitAsync(timeout);
        s1.Value.Origin.Should().Be(s2.OwnOrigin);

        s1.Value = "x1";
        s2.Value = "x2";
        await s1.WhenWritten().WaitAsync(timeout);
        await s2.WhenWritten().WaitAsync(timeout);
        await Task.Delay(timeout);
        s1.Value.Should().Be(s2.Value);
        s1.Value.Origin.Should().Be(s2.Value.Origin);
    }

    [Theory]
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
        var kvas = services.GetRequiredService<TestComputedKvas>();
        var stateFactory = services.StateFactory();
        var updateDelayer = FixedDelayer.NextTick;
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 5);

        for (var i = 0; i < propertyCount; i++) {
            using var cts = new CancellationTokenSource(timeout);
            var cancellationToken = cts.Token;
            using var state = stateFactory.NewKvasSynced<Box<bool>>(new (kvas, $"{keyPrefix}.b{i}") {
                InitialValue = Box.New(false),
                UpdateDelayer = updateDelayer,
            });
            state.Value = Box.New(true);
            if (mustUpdateBeforeWaiting)
                await state.Update(cancellationToken);
            await state.Computed.When(x => x.Value, cancellationToken);
        }
    }

    [Fact]
    public async Task ValueOrDefaultMustReturnNewValueAfterUpdate()
    {
        // arrange
        var rsg = new RandomStringGenerator(alphabet: Alphabet.AlphaNumericLower);
        var services = CreateServices();
        var kvas = services.GetRequiredService<TestComputedKvas>();
        var stateFactory = services.StateFactory();
        var timeout = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 10 : 1);
        var updateDelayer = FixedDelayer.NextTick;

        using var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;
        using var state = stateFactory.NewKvasSynced<Box<bool>>(new(kvas, $"{rsg.Next(5)}.b1") {
            InitialValue = Box.New(false),
            UpdateDelayer = updateDelayer,
        });

        // act
        state.Value = Box.New(true);
        await state.Computed.When(x => x.Value, cancellationToken);

        // assert
        await TestExt.When(() => (state.ValueOrDefault?.Value ?? false).Should().BeTrue(), TimeSpan.FromSeconds(10));
    }
}

[DataContract]
public sealed partial record StringState(
    [property: DataMember(Order = 0)] string Value,
    [property: DataMember(Order = 1)] string Origin = ""
    ) : IHasOrigin
{
    public static implicit operator StringState(string value) => new (value);
}
