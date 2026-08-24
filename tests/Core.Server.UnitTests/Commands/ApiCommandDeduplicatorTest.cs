using System.Diagnostics.Metrics;

namespace ActualChat.Commands.UnitTests;

public sealed class TestCounterService
    : ICommandHandler<TestCounter_Increment>, ICommandHandler<TestCounter_IncrementNotDeduplicated>
{
    private int _value;
    public int HandlerCalls;
    public int Value => _value;

    public Task OnCommand(TestCounter_Increment command, CommandContext context, CancellationToken cancellationToken)
        => Increment(command.Amount, context);

    public Task OnCommand(
        TestCounter_IncrementNotDeduplicated command, CommandContext context, CancellationToken cancellationToken)
        => Increment(command.Amount, context);

    // Private methods

    private Task Increment(int amount, CommandContext context)
    {
        Interlocked.Increment(ref HandlerCalls);
        var newValue = Interlocked.Add(ref _value, amount);
        context.SetResult(newValue);
        return Task.CompletedTask;
    }
}

public class ApiCommandDeduplicatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        var commander = services.AddCommander();
        services.AddFusion();
        services.AddSingleton<IdempotencyStore>();
        services.AddSingleton<ApiCommandDeduplicator>();
        commander.AddHandlers<ApiCommandDeduplicator>();
        services.AddSingleton<TestCounterService>();
        commander.AddHandlers<TestCounterService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SameUuidRunsHandlerOnceAndReplaysResult()
    {
        // arrange
        var services = CreateServices();
        var commander = services.Commander();
        var counter = services.GetRequiredService<TestCounterService>();
        var command = new TestCounter_Increment { Session = Session.New(), Amount = 5 };

        // act
        var result1 = await commander.Call(command, CancellationToken.None);
        var result2 = await commander.Call(command, CancellationToken.None); // same Uuid -> deduped

        // assert
        counter.HandlerCalls.Should().Be(1);
        result1.Should().Be(5);
        result2.Should().Be(5);
        counter.Value.Should().Be(5);
    }

    [Fact]
    public async Task DifferentUuidRunsEachTime()
    {
        // arrange
        var services = CreateServices();
        var commander = services.Commander();
        var counter = services.GetRequiredService<TestCounterService>();
        var session = Session.New();

        // act
        await commander.Call(new TestCounter_Increment { Session = session, Amount = 5 }, CancellationToken.None);
        await commander.Call(new TestCounter_Increment { Session = session, Amount = 5 }, CancellationToken.None);

        // assert
        counter.HandlerCalls.Should().Be(2);
        counter.Value.Should().Be(10);
    }

    [Fact]
    public async Task RecordsDedupOutcomeMetrics()
    {
        // arrange
        var services = CreateServices();
        var commander = services.Commander();
        var counts = new ConcurrentDictionary<string, long>();
        using var listener = new MeterListener {
            InstrumentPublished = (instrument, l) => {
                if (instrument.Name == "command.dedup.outcome")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => {
            foreach (var tag in tags) {
                if (tag.Key == "outcome" && tag.Value is string outcome)
                    counts.AddOrUpdate(outcome, value, (_, v) => v + value);
            }
        });
        listener.Start();
        var command = new TestCounter_Increment { Session = Session.New(), Amount = 5 };

        // act
        await commander.Call(command, CancellationToken.None);
        await commander.Call(command, CancellationToken.None); // duplicate -> replayed

        // assert
        counts.GetValueOrDefault("executed").Should().Be(1);
        counts.GetValueOrDefault("replayed").Should().Be(1);
    }

    [Fact]
    public async Task NotDeduplicatedCommandRunsEveryTime()
    {
        // arrange
        var services = CreateServices();
        var commander = services.Commander();
        var counter = services.GetRequiredService<TestCounterService>();
        var command = new TestCounter_IncrementNotDeduplicated { Session = Session.New(), Amount = 5 };

        // act
        await commander.Call(command, CancellationToken.None);
        await commander.Call(command, CancellationToken.None);

        // assert
        counter.HandlerCalls.Should().Be(2);
        counter.Value.Should().Be(10);
    }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TestCounter_Increment : ApiCommand<int>
{
    [DataMember(Order = 2), Key(2)] public required int Amount { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TestCounter_IncrementNotDeduplicated : ApiCommand<int>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public required int Amount { get; init; }
}
