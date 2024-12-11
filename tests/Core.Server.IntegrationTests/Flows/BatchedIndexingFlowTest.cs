using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
public class BatchedIndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private long _lid = 1;
    private const int BatchSize = SimpleBatchedIndexingFlow.BatchSizeOverride;
    private const int Quota = SimpleBatchedIndexingFlow.QuotaOverride;
    private static readonly TimeSpan RecheckInterval = SimpleBatchedIndexingFlow.RecheckIntervalOverride;
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
        var start = Clocks.SystemClock.Now;

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEmpty();
            Context.ListRemaining(id).Should().BeEmpty();
            var (step, hardResumeIn) = Context.ListTransitions(id, start).Should().HaveCount(1).And.Subject.First();
            step.Should().Be("OnIndex");
            hardResumeIn.Should().BeCloseTo(RecheckInterval, TimeSpan.FromMinutes(3));
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
    public async Task MustProcessAllBatches(int fullBatchCount, int lastBatchSize)
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
        var start = Clocks.SystemClock.Now;

        // assert
        var processedQuotaCount = fullBatchCount * BatchSize / Quota;
        await TestExt.When(async () => {
            Context.ListProcessed(id).Should();
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id);
            transitions
                .Should()
                .HaveCount(processedQuotaCount + 1);
            transitions[..^1].Should().AllBeEquivalentTo(("OnIndex", (TimeSpan?)null));
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));
            var flow = await Flows.Get<SimpleBatchedIndexingFlow>(id);
            (flow!.NextRecheckAt - start).Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));
        }, TimeSpan.FromSeconds(10));

        // assert
        await TestExt.When(async () => {
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id, start);
            transitions
                .Should()
                .HaveCount(processedQuotaCount + 2);
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
            var flow = await Flows.Get<SimpleBatchedIndexingFlow>(id);
            flow!.NextRecheckAt.Should().BeNull();
        }, TimeSpan.FromSeconds(10));
    }

    private List<SimpleItem> NewBatch(int lastBatchSize)
        => Enumerable.Range(1, lastBatchSize).Select(NewItem).ToList();

    private SimpleItem NewItem(int i = -1)
        => new (new ChatId(Generate.Option), $"Entry {_lid++} {(i >= 0 ? i : null)}");
}
