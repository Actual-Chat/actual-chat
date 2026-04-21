using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Trait("Category", "Slow")]
public class SessionTemporalsMeshTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(SessionTemporalsMeshTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task HostAdditionTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(15);
        var session = Session.New();
        var key = Constants.SessionTemporals.SignInErrorKey;
        var error = "Account not found.";

        // Start h1, set a value
        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var commander1 = h1.Services.Commander();
        await w1.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        await commander1.Call(new SessionTemporalsBackend_Set(session, key, error));
        var s1 = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
        (await s1.Get(session, key, default)).Should().Be(error);

        // Add h2, wait for mesh sync
        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);

        // h2 should be able to read the value (Redis-backed)
        var s2 = h2.Services.GetRequiredService<ISessionTemporalsBackend>();
        var value = await s2.Get(session, key, default);
        value.Should().Be(error);
    }

    [Fact(Timeout = 60_000)]
    public async Task HostRemovalTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(15);
        var session = Session.New();
        var key = Constants.SessionTemporals.SignInErrorKey;
        var error = "Account not found.";

        // Start two hosts
        await using var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.LiveNodes.Length == 2).WaitAsync(syncTimeout);

        // Set value via h1
        var commander1 = h1.Services.Commander();
        await commander1.Call(new SessionTemporalsBackend_Set(session, key, error));

        // Verify both can read it
        var s1 = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
        var s2 = h2.Services.GetRequiredService<ISessionTemporalsBackend>();
        (await s1.Get(session, key, default)).Should().Be(error);
        (await s2.Get(session, key, default)).Should().Be(error);

        // Remove h1
        await h1.DisposeAsync();
        await w2.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        // h2 should still read the value from Redis after shard takeover
        await ComputedTest.When(async ct => {
            var value = await s2.Get(session, key, ct);
            value.Should().Be(error);
        }, syncTimeout);
    }

    [Fact(Timeout = 60_000)]
    public async Task WriteOnOneHost_ReadOnAnotherAfterRestartTest()
    {
        var syncTimeout = TimeSpan.FromSeconds(15);
        var session = Session.New();
        var key = "cross-host-key";

        // Start h1, set a value, then shut it down
        var h1 = await NewAppHost();
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        await w1.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        var commander1 = h1.Services.Commander();
        await commander1.Call(new SessionTemporalsBackend_Set(session, key, "persisted"));
        await h1.DisposeAsync();

        // Start h2 from scratch — it should read the value from Redis
        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        await w2.State.Computed.When(x => x.LiveNodes.Length == 1).WaitAsync(syncTimeout);

        var s2 = h2.Services.GetRequiredService<ISessionTemporalsBackend>();
        var value = await s2.Get(session, key, default);
        value.Should().Be("persisted");
    }
}
