using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
[Trait("Category", "Slow")]
public sealed class IndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    // Each batch costs a full commit -> event -> resume round-trip, ~40ms locally but up to
    // ~800ms on a loaded CI runner, so the budget has to scale with the batch count
    private static readonly TimeSpan BaseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeoutPerBatch = TimeSpan.FromSeconds(1);
    private IndexingFlowTestContext Context { get; }
        = fixture.AppHost.Services.GetRequiredService<IndexingFlowTestContext>();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(77)]
    public async Task ShouldProcessAllBatches(int batchCount)
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        var batchSize = 10;
        var batches = Enumerable.Range(1, batchCount)
            .Select(i => {
                var cursor = i * batchSize;
                return new BatchIndexingResult<long> {
                    Cursor = cursor,
                    IsTailReached = false,
                    HasProcessedAnyItems = true,
                };
            })
            .Append(new BatchIndexingResult<long> {
                Cursor = (batchCount + 1) * batchSize,
                IsTailReached = true,
                HasProcessedAnyItems = true,
            })
            .ToList();
        Context.Add(id, batches);

        // act
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).Schedule();

        // assert
        await TestExt.When(async () => {
            Context.ListRemaining(id).Should().BeEmpty("every batch must be consumed");
            var processed = Context.ListProcessed(id);
            processed.Should().HaveCount(batchCount + 1, "every batch must be processed");
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id);
            flow.Should().NotBeNull("the scheduled resume must create the flow");
            flow.Cursor.Should().Be((batchCount + 1) * batchSize, "the cursor must reach the tail");
        }, GetTimeout(batchCount));
    }

    [Fact]
    public async Task ShouldCompleteOnCompletionReason()
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new() {
                Cursor = 20,
                IsTailReached = false,
                HasProcessedAnyItems = true,
                CompletionReason = "Done.",
            },
            new() {
                Cursor = 30,
                IsTailReached = false,
                HasProcessedAnyItems = true,
            },
        ];
        Context.Add(id, batches);

        // act
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).Schedule();

        // assert
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id);
            flow.Should().NotBeNull("the scheduled resume must create the flow");
            flow.Result.Should().Be(Result.New("Done."), "the completion reason must end the flow");
            Context.ListRemaining(id).Should().BeEquivalentTo(batches[1..], "the flow must stop at the first batch");
        }, GetTimeout(batches.Length));
    }

    [Fact]
    public async Task ShouldReindexOnReset()
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new() {
                Cursor = 20,
                IsTailReached = true,
                HasProcessedAnyItems = true,
            },
            new() {
                Cursor = 30,
                IsTailReached = true,
                HasProcessedAnyItems = true,
            },
            new() {
                Cursor = 30,
                IsTailReached = true,
                HasProcessedAnyItems = true,
            },
            new() {
                Cursor = 30,
                IsTailReached = true,
                HasProcessedAnyItems = true,
            },
        ];
        Context.Add(id, batches);
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).Schedule();
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id).Require();
            flow.DataVersion.Should().Be(1, "the flow must run at its current data version");
        }, GetTimeout(batches.Length));

        // act
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).WithReset().Schedule();

        // assert
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id).Require();
            flow.Console.ToString().Should().Contain("explicit", "the reset must restart the indexing");
        }, GetTimeout(batches.Length));
    }

    // Private methods

    private static TimeSpan GetTimeout(int batchCount)
        => BaseTimeout + (TimeoutPerBatch * batchCount);
}
