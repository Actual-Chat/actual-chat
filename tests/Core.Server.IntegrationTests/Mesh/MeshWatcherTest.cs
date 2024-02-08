using ActualChat.Mesh;
using ActualChat.Rpc;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

[Collection(nameof(ServerCollection)), Trait("Category", nameof(ServerCollection))]
public class MeshWatcherTest(AppHostFixture fixture, ITestOutputHelper @out)
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.UseOutput(@out);

    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(3);

        using var h1 = await NewAppHost(TestAppHostOptions.None);
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s = w1.State.Value.GetShardMap<ShardScheme.Backend>();
        Out.WriteLine(s.ToString());

        await w1.State.When(x => x.Nodes.Length == 1).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap<ShardScheme.Backend>();
        Out.WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        using var h2 = await NewAppHost(TestAppHostOptions.None);
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w1.State.When(x => x.Nodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.When(x => x.Nodes.Length == 2).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap<ShardScheme.Backend>();
        Out.WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        _ = w1.DisposeAsync();
        await w1.State.When((_, e) => e is ObjectDisposedException).WaitAsync(syncTimeout);
        await w2.State.When(x => x.Nodes.Length == 1).WaitAsync(syncTimeout);

        _ = w2.DisposeAsync();
        await w2.State.When((_, e) => e is ObjectDisposedException).WaitAsync(syncTimeout);
    }

    [Fact(Timeout = 30_000)]
    public async Task PeerNodeRefTest()
    {
        using var h1 = await NewAppHost(TestAppHostOptions.None);
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();

        using var h2 = await NewAppHost(TestAppHostOptions.None);
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();

        var w1w2 = w1.GetPeerRef(w2.ThisNode.Ref).Require();
        var w2w1 = w2.GetPeerRef(w1.ThisNode.Ref).Require();

        _ = w2.DisposeAsync();

        var t1a = Task.Delay(w1.ChangeTimeout * 0.5, w1w2.StopToken);
        var t1b = Task.Delay(w1.ChangeTimeout * 1.5, w1w2.StopToken);
        var t2 = Task.Delay(TimeSpan.FromSeconds(1), w2w1.StopToken);
        var r1a = await t1a.ResultAwait();
        var r1b = await t1b.ResultAwait();
        var r2 = await t2.ResultAwait();
        r1a.Error.Should().BeNull();
        (r1b.Error is OperationCanceledException).Should().BeTrue();
        (r2.Error is OperationCanceledException).Should().BeTrue();
    }

    // Private methods

    private Task<TestAppHost> NewAppHost(TestAppHostOptions? options = default)
        => TestAppHostFactory.NewAppHost(Out, options);
}
