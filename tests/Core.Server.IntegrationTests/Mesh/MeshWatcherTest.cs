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
        w1.Start();
        await w1.State.When(x => x.Nodes.Length == 1).WaitAsync(syncTimeout);

        using var h2 = await NewAppHost(TestAppHostOptions.None);
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        w2.Start();
        await w1.State.When(x => x.Nodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.When(x => x.Nodes.Length == 2).WaitAsync(syncTimeout);

        _ = w1.DisposeAsync();
        await w1.State.When(_ => false).WaitAsync(syncTimeout).SilentAwait(); // Must be cancelled
        await w2.State.When(x => x.Nodes.Length == 1).WaitAsync(syncTimeout);

        _ = w2.DisposeAsync();
        await w2.State.When(_ => false).WaitAsync(syncTimeout).SilentAwait(); // Must be cancelled
    }
}
