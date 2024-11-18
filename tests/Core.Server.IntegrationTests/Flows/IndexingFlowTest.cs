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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(77)]
    public async Task MustProcessAllBatches(int batchCount)
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        var batchSize = 10;
        var batches = Enumerable.Range(1, batchCount)
            .Select(i => new BatchIndexingResult<long>(false, false, i * batchSize, batchSize))
            .Append(new (false, true, (batchCount + 1) * batchSize, batchSize))
            .ToList();
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListRemaining(id).Should().BeEmpty();
            var transitions = Context.ListTransitions(id);
            transitions
                .Should()
                .HaveCount(batchCount + 1);
            transitions[..^1].Should().AllBeEquivalentTo(("OnIndex", (TimeSpan?)null));
            transitions[^1].Step.Should().Be("OnIndex");
            transitions[^1].HardResumeIn.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MustEnd()
    {
        // arrange
        var id = RandomSymbolGenerator.Default.Next();
        BatchIndexingResult<long>[] batches = [
            new (true, false, 20, 10),
            new (false, false, 30, 10),
        ];
        Context.Add(id, batches);

        // act
        await Flows.GetOrStart<SimpleIndexingFlow>(id);

        // assert
        await TestExt.When(() => {
            Context.ListTransitions(id).Should().BeEquivalentTo([("OnReset", TimeSpan.MaxValue)]);
            Context.ListRemaining(id).Should().BeEquivalentTo(batches[1..]);
        }, TimeSpan.FromSeconds(10));
    }
}
