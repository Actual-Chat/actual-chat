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
        await w2.State.Computed.When(x => x.AllNodes.Count == 1).WaitAsync(syncTimeout);
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

        // ShardRef test: pick a peer ref on h1 that routes to a shard owned by w2.
        // When w2 goes down, that peer ref's RouteState must flip to 'changed' so
        // outbound calls reroute. We don't test the symmetric sr1-on-h2 case because
        // disposing w2 also tears down h2's MeshState, which can legitimately cascade
        // into MarkChanged on peer refs living on h2 — spurious MarkChanged is fine,
        // missing MarkChanged is the only real bug.
        MeshRpcPeerRef? sr2 = null;
        for (var i = 0; i < ShardScheme.FlowsBackend.ShardCount; i++) {
            if (sr2?.NodeRef != w2.ThisNode.Ref)
                sr2 = refs1.Get(new ShardRef(ShardScheme.FlowsBackend, i)).Require();
        }
        _ = w2.DisposeAsync();

        // With no Offline grace period, the route state should change
        // as soon as the lock expires and the watcher detects it
        var t1 = Task.Delay(TimeSpan.FromSeconds(10), sr2!.RouteState!.ChangedToken);
        var r1 = await t1.ResultAwait();
        (r1.Error is OperationCanceledException).Should().BeTrue();
    }
}
