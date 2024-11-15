using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
public class BatchedIndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private long _lid = 1;
    private static readonly TimeSpan InfiniteHardResumeIn = TimeSpan.MaxValue;
    private const int BatchSize = SimpleBatchedIndexingFlow.BatchSizeOverride;
    private const int Quota = SimpleBatchedIndexingFlow.QuotaOverride;
    private BatchedIndexingFlowTestContext<SimpleItem> Context { get; } = fixture.AppHost.Services.GetRequiredService<BatchedIndexingFlowTestContext<SimpleItem>>();

    [Fact]
    public async Task MustHandleEmptyBatch()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [],
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEmpty();
            Context.ListRemaining(id).Should().BeEmpty();
            var (step, hardResumeIn) = Context.ListTransitions(id).Should().HaveCount(1).And.Subject.First();
            step.Should().Be("OnIndex");
            hardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustHandleEmptyBatchAndSuspendWhenTailIsCompletion()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [],
        ];
        Context.Add(id, batches);
        Context.AddTailHandler(id, () => ActualLab.Async.TaskExt.FalseTask);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEmpty();
            Context.ListRemaining(id).Should().BeEmpty();
            Context.ListTransitions(id).Should().BeEquivalentTo([("OnReset", InfiniteHardResumeIn)]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustScheduleWatchdogTimerWhenQuotaIsNotExceeded()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem()],
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEquivalentTo(batches);
            var (step, hardResumeIn) = Context.ListTransitions(id).Should().HaveCount(1).And.Subject.First();
            step.Should().Be("OnIndex");
            hardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(7, 0)]
    [InlineData(7, 1)]
    [InlineData(7, 2)]
    public async Task MustProcessAllBatchesByRequests(int fullBatchCount, int lastBatchSize)
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        var batches = Enumerable.Range(1, fullBatchCount)
            .Select(_ => NewBatch(BatchSize))
            .Append(NewBatch(lastBatchSize))
            .ToList();
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should();
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id);
            transitions
                .Should()
                .HaveCount((fullBatchCount * BatchSize / Quota) + 1);
            transitions[..^1].Should().AllBeEquivalentTo(("OnIndex", (TimeSpan?)null));
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
        }, TimeSpan.FromSeconds(10));
    }

    private List<SimpleItem> NewBatch(int lastBatchSize)
        => Enumerable.Range(1, lastBatchSize).Select(NewItem).ToList();

    private SimpleItem NewItem(int i = -1)
        => new (new ChatId(Generate.Option), $"Entry {_lid++} {(i >= 0 ? i : null)}");
}
