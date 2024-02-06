using ActualChat.Mesh;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class MeshWatcherTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
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
}
