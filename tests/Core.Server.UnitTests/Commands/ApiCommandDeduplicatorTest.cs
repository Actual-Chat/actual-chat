using System.Diagnostics.Metrics;

namespace ActualChat.Commands.UnitTests;

public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (string Owner, ReadOnlyMemory<byte>? Result)> _entries = new();

    public Task<IdempotencyEntry> ClaimOrGet(string key, string owner, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (_entries.TryAdd(key, (owner, null)))
            return Task.FromResult(new IdempotencyEntry(IdempotencyState.New, Owner: owner));

        var e = _entries[key];
        return Task.FromResult(e.Result is { } bytes
            ? new IdempotencyEntry(IdempotencyState.Completed, bytes)
            : new IdempotencyEntry(IdempotencyState.InProgress, Owner: e.Owner));
    }

    public Task Complete(string key, ReadOnlyMemory<byte> resultMessage, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _entries[key] = ("", resultMessage);
        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>?> WaitForResult(string key, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(_entries.TryGetValue(key, out var e) && e.Result is { } b ? (ReadOnlyMemory<byte>?)b : null);

    public Task<IdempotencyEntry?> TryReclaim(
        string key, string expectedOwner, string newOwner, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(key, out var e))
            return Task.FromResult<IdempotencyEntry?>(null);
        if (e.Result is { } bytes)
            return Task.FromResult<IdempotencyEntry?>(new IdempotencyEntry(IdempotencyState.Completed, bytes));
        if (e.Owner == expectedOwner) {
            _entries[key] = (newOwner, null);
            return Task.FromResult<IdempotencyEntry?>(new IdempotencyEntry(IdempotencyState.New, Owner: newOwner));
        }
        return Task.FromResult<IdempotencyEntry?>(null);
    }

    public Task Release(string key, CancellationToken cancellationToken)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public sealed class TestCounterService : ICommandHandler<TestCounter_Increment>
{
    private int _value;

    public int Value => _value;
    public int HandlerCalls;

    public Task OnCommand(TestCounter_Increment command, CommandContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref HandlerCalls);
        var newValue = Interlocked.Add(ref _value, command.Amount);
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
        services.AddSingleton<IIdempotencyStore, FakeIdempotencyStore>();
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
        var command = new TestCounter_Increment { Session = Session.New(), Amount = 5 }; // Uuid auto-generated once

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
        await commander.Call(new TestCounter_Increment { Session = session, Amount = 5 }, CancellationToken.None); // different Uuid

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
            foreach (var tag in tags)
                if (tag.Key == "outcome" && tag.Value is string outcome)
                    counts.AddOrUpdate(outcome, value, (_, v) => v + value);
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
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TestCounter_Increment : ApiCommand<int>
{
    [DataMember(Order = 2), Key(2)] public required int Amount { get; init; }
}
