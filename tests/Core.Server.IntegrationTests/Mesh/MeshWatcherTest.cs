using ActualChat.Rpc;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class MeshWatcherTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(MeshWatcherTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(10);

        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        WriteLine(s.ToString());

        await w1.State.Computed.When(x => x.AllNodes.Count == 1).WaitAsync(syncTimeout);
        await w1.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w1.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        s = w1.State.Value.GetShardMap(ShardScheme.TestBackend);
        WriteLine(s.ToString());
        s.IsEmpty.Should().BeFalse();

        _ = w1.DisposeAsync();
        await w1.State.Computed.When(x => x.IsFinal).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        _ = w2.DisposeAsync();
        await w2.State.Computed.When(x => x.IsFinal).WaitAsync(syncTimeout);
    }

    [Fact(Timeout = 30_000)]
    public async Task PeerNodeRefTest()
    {
        var timeout = TimeSpan.FromSeconds(5);

        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var refs1 = h1.Services.GetRequiredService<MeshRpcPeerRefs>();

        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        var refs2 = h2.Services.GetRequiredService<MeshRpcPeerRefs>();

        // Wait for both hosts to see each other
        await w1.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(timeout);
        await w2.State.Computed.When(x => x.AllNodes.Count == 2).WaitAsync(timeout);
        await Task.Delay(TimeSpan.FromSeconds(1));

        // NoteRef test
        refs1.Get(w2.ThisNode.Ref).Require().RouteState.Should().BeNull();
        refs2.Get(w1.ThisNode.Ref).Require().RouteState.Should().BeNull();

        // ShardRef test
        MeshRpcPeerRef? sr1 = null;
        MeshRpcPeerRef? sr2 = null;
        for (var i = 0; i < ShardScheme.FlowsBackend.ShardCount; i++) {
            if (sr1?.NodeRef != w1.ThisNode.Ref)
                sr1 = refs2.Get(new ShardRef(ShardScheme.FlowsBackend, i)).Require();
            if (sr2?.NodeRef != w2.ThisNode.Ref)
                sr2 = refs1.Get(new ShardRef(ShardScheme.FlowsBackend, i)).Require();
        }
        _ = w2.DisposeAsync();

        // With no Offline grace period, the route state should change
        // as soon as the lock expires and the watcher detects it
        var t1 = Task.Delay(TimeSpan.FromSeconds(10), sr2!.RouteState!.ChangedToken);
        var t2 = Task.Delay(TimeSpan.FromSeconds(1), sr1!.RouteState!.ChangedToken);
        var r1 = await t1.ResultAwait();
        var r2 = await t2.ResultAwait();
        (r1.Error is OperationCanceledException).Should().BeTrue();
        r2.Error.Should().BeNull();
    }
}
