using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Collection(nameof(ServerCollection))]
[Trait("Category", "Slow")]
public sealed class FlowStoreTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IndexingFlowTestContext Context { get; }
        = fixture.AppHost.Services.GetRequiredService<IndexingFlowTestContext>();

    [Fact]
    public async Task StaleStoreShouldKeepTheStoredFlow()
    {
        // arrange
        var id = RandomStringGenerator.Default.Next();
        Context.Add(id, new BatchIndexingResult<long> {
            Cursor = 10,
            IsTailReached = true,
            HasProcessedAnyItems = true,
        });
        await FlowHub.NewResumeEvent<SimpleIndexingFlow>(id).Schedule();
        var flow = await TestWait.WhenPolled<SimpleIndexingFlow>(async () => {
            var f = await FlowHub.TryGet<SimpleIndexingFlow>(id);
            f.Should().NotBeNull("the scheduled resume must create the flow");
            f.Cursor.Should().Be(10, "the flow must reach the tail");
            return f;
        });
        var storedVersion = flow.Version;

        // act
        var store = new Flows_Store(flow.Id, storedVersion + 1) { Flow = flow };
        var version = await Commander.Call(store);

        // assert
        version.Should().Be(storedVersion, "a stale store returns the stored version");
        // Any write bumps the version; the flow's fields can't tell, as TryGet hands out a shared instance
        var flowData = await AppHost.Services.GetRequiredService<IFlowBackend>().TryGetData(flow.Id, default);
        flowData.Should().NotBeNull();
        flowData.Version.Should().Be(storedVersion, "a stale store must not write");
    }
}
