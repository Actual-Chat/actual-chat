using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
public class BatchedIndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private long _lid = 1;
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
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
            Context.ListRemaining(id).Should().BeEmpty();
        }, TimeSpan.FromSeconds(60));
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
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].HardResumeAt.Should().Be(Flow.InfiniteHardResumeAt);
            transitions[0].Step.Should().Be("OnReset");
            Context.ListRemaining(id).Should().BeEquivalentTo(batches[1..]);
            Context.ListRemaining(id).Should().BeEmpty();
        }, TimeSpan.FromSeconds(60));
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
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
            Context.ListRemaining(id).Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustWaitForTimerWhenQuotaIsExceededAndNoRemainingBatches()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEquivalentTo(batches[..2]);
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
            Context.ListRemaining(id).Should().Equal(batches[2..]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustWaitForTimerWhenQuotaIsExceededAndWithRemainingBatches()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem()],
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListProcessed(id).Should().BeEquivalentTo(batches[..2]);
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
            Context.ListRemaining(id).Should().Equal(batches[2..]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustProcessAllBatchesByRequests()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        IReadOnlyList<SimpleItem>[] batches = [
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem(), NewItem()],
            [NewItem(), NewItem()],
        ];
        Context.Add(id, batches);

        // act
        var flow = await Flows.GetOrStart<SimpleBatchedIndexingFlow>(id);

        for (int i = 0; i < batches.Length / 2; i++) {
            // act
            if (i > 0)
                await Queues.Enqueue(new FlowResumeEvent(flow.Id));

            // assert
            var batchIndex = (i + 1) * 2;
            await TestExt.When(() => {
                Context.ListProcessed(id).Should().Equal(batches[..batchIndex]);
                Context.ListRemaining(id).Should().Equal(batches[batchIndex..]);
            }, TimeSpan.FromSeconds(10));
        }
    }

    private SimpleItem NewItem()
        => new (new ChatId(Generate.Option), $"Entry {_lid++}");
}
