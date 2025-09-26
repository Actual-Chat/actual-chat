using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
public class IndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan RecheckInterval = SimpleBatchedIndexingFlow.RecheckIntervalOverride;
    private IndexingFlowTestContext Context { get; } = fixture.AppHost.Services.GetRequiredService<IndexingFlowTestContext>();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(77)]
    public async Task MustProcessAllBatches(int batchCount)
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        var batchSize = 10;
        var batches = Enumerable.Range(1, batchCount)
            .Select(i => new BatchIndexingResult<long>(false, false, i * batchSize, true))
            .Append(new (false, true, (batchCount + 1) * batchSize, true))
            .ToList();
        Context.Add(id, batches);

        // act
        await Flows.Get<SimpleIndexingFlow>(id);
        var start = Clocks.SystemClock.Now;

        // assert
        await TestExt.When(async () => {
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id, start);
            transitions
                .Should()
                .HaveCount(batchCount + 1);
            transitions[..^1].Should().AllBeEquivalentTo(("OnIndex", (TimeSpan?)null));
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));
            var flow = await Flows.TryGet<SimpleIndexingFlow>(id);
            (flow!.NextRecheckAt - start).Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));
        }, TimeSpan.FromSeconds(10));

        // act
        Context.Add(id, [new (false, true, (batchCount + 1) * batchSize, false)]);

        // assert
        await TestExt.When(async () => {
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id, start);
            transitions
                .Should()
                .HaveCount(batchCount + 2);
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
            var flow = await Flows.TryGet<SimpleIndexingFlow>(id);
            flow!.NextRecheckAt.Should().BeNull();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustEnd()
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (true, false, 20, true),
            new (false, false, 30, true),
        ];
        Context.Add(id, batches);

        // act
        await Flows.Get<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListTransitions(id).Should().BeEquivalentTo([("OnReset", TimeSpan.MaxValue)]);
            Context.ListRemaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustWaitForReindexingIfVersionIsBumped()
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (false, true, 20, true),
            new (false, true, 30, true),
            new (false, true, 30, true),
            new (false, true, 30, true),
        ];
        Context.Add(id, batches);

        // act
        await Flows.Get<SimpleIndexingFlow>(id);
        var start = Clocks.SystemClock.Now;

        // assert
        await TestExt.When(async () => {
            var flow = await Flows.TryGet<SimpleIndexingFlow>(id).Require();
            flow.FlowSetVersion.Should().Be(1);
            var transitions = Context.ListTransitions(id, start);
            transitions
                .Should()
                .HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeIn.Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));
        }, TimeSpan.FromSeconds(10));

        // act
        Context.SetCurrentFlowSetVersionOverride(id, 2);
        await Flows.Resume<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(async () => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(2);
            transitions[^1].Should().BeEquivalentTo(("", (TimeSpan?)null));

            var flow = await Flows.TryGet<SimpleIndexingFlow>(id).Require();
            flow.FlowSetVersion.Should().Be(1);
        }, TimeSpan.FromSeconds(10));

        // act
        await Queues.Enqueue(new FlowResetEvent(FlowRegistry.NewId<SimpleIndexingFlow>(id)));

        // assert
        await TestExt.When(async () => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(4);
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(RecheckInterval, TimeSpan.FromSeconds(3));

            var flow = await Flows.TryGet<SimpleIndexingFlow>(id).Require();
            flow.FlowSetVersion.Should().Be(2);
        }, TimeSpan.FromSeconds(10));
    }
}
