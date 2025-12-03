using ActualChat.Rpc;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class MeshWatcherTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(MeshWatcherTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        Out.WriteLine(s.ToString());

        var syncTimeout = w1.OfflineTimeout * 2;
        await w1.State.Computed.When(x => x.AllNodes.Count == 1).WaitAsync(syncTimeout);
        await w1.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        Out.WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w1.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        Out.WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        _ = w1.DisposeAsync();
        await w1.State.Computed.When(x => x.IsFinal).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        _ = w2.DisposeAsync();
        await w1.State.Computed.When(x => x.IsFinal).WaitAsync(syncTimeout);
    }

    [Fact(Timeout = 30_000, Skip = "Unstable. AY should fix it.")]
    public async Task PeerNodeRefTest()
    {
        using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var c1 = h1.Services.GetRequiredService<MeshRpcPeerRefs>();

        using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        var c2 = h2.Services.GetRequiredService<MeshRpcPeerRefs>();

        var w1w2 = c1.Get(w2.ThisNode.Ref).Require();
        var w2w1 = c2.Get(w1.ThisNode.Ref).Require();

        _ = w2.DisposeAsync();

        var t1a = Task.Delay(w1.OfflineTimeout * 0.5, w1w2.RouteState!.ChangedToken);
        var t1b = Task.Delay(w1.OfflineTimeout * 1.5, w1w2.RouteState!.ChangedToken);
        var t2 = Task.Delay(TimeSpan.FromSeconds(1), w2w1.RouteState!.ChangedToken);
        var r1a = await t1a.ResultAwait();
        var r1b = await t1b.ResultAwait();
        var r2 = await t2.ResultAwait();
        r1a.Error.Should().BeNull();
        (r1b.Error is OperationCanceledException).Should().BeTrue();
        r2.Error.Should().BeNull();
    }
}
