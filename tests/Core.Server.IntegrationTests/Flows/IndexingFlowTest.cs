using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
[Trait("Category", "Slow")]
public class IndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IndexingFlowTestContext Context { get; } = fixture.AppHost.Services.GetRequiredService<IndexingFlowTestContext>();

    [FlakyTheory("AY: Not sure why yet.", 3)]
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
            .Select(i => {
                long cursor = i * batchSize;
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
        await FlowHub.Get<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(async () => {
            Context.ListRemaining(id).Should().BeEmpty();
            var processed = Context.ListProcessed(id);
            processed.Should().HaveCount(batchCount + 1);
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id);
            flow.Should().NotBeNull();
            flow.Cursor.Should().Be((batchCount + 1) * batchSize);
        }, TimeSpan.FromSeconds(10));
    }

    [FlakyFact("AY: Not sure why yet.", 3)]
    public async Task MustEnd()
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
        await FlowHub.Get<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id);
            flow.Should().NotBeNull();
            flow.Result.Should().Be(Result.New("Done."));
            Context.ListRemaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }

    [FlakyFact("AY: Not sure why yet.", 3)]
    public async Task MustWaitForReindexingIfVersionIsBumped()
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

        // act
        await FlowHub.Get<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id).Require();
            flow.DataVersion.Should().Be(1);
        }, TimeSpan.FromSeconds(10));

        // act - bump version to trigger reindex
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).WithReset().Schedule();

        // assert - flow should reindex (reset cursor and process again)
        await TestExt.When(async () => {
            var flow = await FlowHub.TryGet<SimpleIndexingFlow>(id).Require();
            flow.Console.ToString().Should().Contain("explicit");
        }, TimeSpan.FromSeconds(10));
    }
}
