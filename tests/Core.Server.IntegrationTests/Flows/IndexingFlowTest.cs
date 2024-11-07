using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
public class IndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IndexingFlowTestContext Context { get; } = fixture.AppHost.Services.GetRequiredService<IndexingFlowTestContext>();

    [Fact]
    public async Task MustProcessBatch()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (false, false, 10),
            new (false, false, 20),
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].Step.Should().Be("OnIndex");
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
            Context.Remaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustProcessAllBatchesByRequests()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (false, false, 10),
            new (false, false, 20),
            new (false, true, 30),
        ];
        Context.Add(id, batches);

        // act
        var flow = await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.Remaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));

        // act
        await Queues.Enqueue(new FlowResumeEvent(flow.Id));

        // assert
        await TestExt.When(() => {
            Context.Remaining(id).Should().BeEquivalentTo(batches[2..]);
        }, TimeSpan.FromSeconds(10));

        // act
        await Queues.Enqueue(new FlowResumeEvent(flow.Id));

        // assert
        await TestExt.When(() => {
            Context.Remaining(id).Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustScheduleWatchdogTimerWhenTailReached()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (false, true, 10),
            new (false, false, 20),
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].HardResumeAt.Should().NotBeNull();
            var resumeIn = transitions[0].HardResumeAt!.Value - Clocks.SystemClock.Now;
            resumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
            Context.Remaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustEnd()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (true, false, 20),
            new (false, false, 20),
        ];
        Context.Add(id, batches);

        // act
        var flow = await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].HardResumeAt.Should().Be(Flow.InfiniteHardResumeAt);
            transitions[0].Step.Should().Be("OnReset");
            Context.Remaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));

        // act
        await Queues.Enqueue(new FlowResumeEvent(flow.Id));

        // assert
        await TestExt.When(() => {
            var transitions = Context.ListTransitions(id);
            transitions.Should().HaveCount(1);
            transitions[0].HardResumeAt.Should().Be(Flow.InfiniteHardResumeAt);
            transitions[0].Step.Should().Be("OnReset");
            Context.Remaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }
}
