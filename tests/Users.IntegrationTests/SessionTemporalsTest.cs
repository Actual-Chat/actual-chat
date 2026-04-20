using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Trait("Category", "Slow")]
public class SessionTemporalsTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(SessionTemporalsTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task BasicTest()
    {
        await using var h1 = await NewAppHost();
        var sessionTemporals = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session = Session.New();
        var key = "test-key";

        // Initially empty
        var value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().BeNull();

        // Set a value
        var commander = h1.Services.Commander();
        await commander.Call(new SessionTemporalsBackend_Set(session, key, "hello"));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().Be("hello");

        // Overwrite
        await commander.Call(new SessionTemporalsBackend_Set(session, key, "world"));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().Be("world");

        // Remove
        await commander.Call(new SessionTemporalsBackend_Set(session, key, null));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().BeNull();
    }

    [Fact(Timeout = 60_000)]
    public async Task MultipleKeysTest()
    {
        await using var h1 = await NewAppHost();
        var sessionTemporals = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
        var commander = h1.Services.Commander();
        var session = Session.New();

        await commander.Call(new SessionTemporalsBackend_Set(session, "a", "1"));
        await commander.Call(new SessionTemporalsBackend_Set(session, "b", "2"));
        await commander.Call(new SessionTemporalsBackend_Set(session, "c", "3"));

        (await sessionTemporals.Get(session, "a", default)).Should().Be("1");
        (await sessionTemporals.Get(session, "b", default)).Should().Be("2");
        (await sessionTemporals.Get(session, "c", default)).Should().Be("3");

        // Remove one, others stay
        await commander.Call(new SessionTemporalsBackend_Set(session, "b", null));
        (await sessionTemporals.Get(session, "a", default)).Should().Be("1");
        (await sessionTemporals.Get(session, "b", default)).Should().BeNull();
        (await sessionTemporals.Get(session, "c", default)).Should().Be("3");
    }

    [Fact(Timeout = 60_000)]
    public async Task SessionIsolationTest()
    {
        await using var h1 = await NewAppHost();
        var sessionTemporals = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
        var commander = h1.Services.Commander();
        var session1 = Session.New();
        var session2 = Session.New();

        await commander.Call(new SessionTemporalsBackend_Set(session1, "key", "value1"));
        await commander.Call(new SessionTemporalsBackend_Set(session2, "key", "value2"));

        (await sessionTemporals.Get(session1, "key", default)).Should().Be("value1");
        (await sessionTemporals.Get(session2, "key", default)).Should().Be("value2");
    }

    [Fact(Timeout = 60_000)]
    public async Task KeyLengthLimitTest()
    {
        await using var h1 = await NewAppHost();
        var commander = h1.Services.Commander();
        var session = Session.New();

        var longKey = new string('k', SessionTemporalsBackend.MaxKeyLength + 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => commander.Call(new SessionTemporalsBackend_Set(session, longKey, "v")));

        // Exactly at limit should work
        var maxKey = new string('k', SessionTemporalsBackend.MaxKeyLength);
        await commander.Call(new SessionTemporalsBackend_Set(session, maxKey, "v"));

    }

    [Fact(Timeout = 60_000)]
    public async Task ValueLengthLimitTest()
    {
        await using var h1 = await NewAppHost();
        var commander = h1.Services.Commander();
        var session = Session.New();

        var longValue = new string('v', SessionTemporalsBackend.MaxValueLength + 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => commander.Call(new SessionTemporalsBackend_Set(session, "key", longValue)));

        // Exactly at limit should work
        var maxValue = new string('v', SessionTemporalsBackend.MaxValueLength);
        await commander.Call(new SessionTemporalsBackend_Set(session, "key", maxValue));
    }

    [Fact(Timeout = 60_000)]
    public async Task EntryCountLimitTest()
    {
        await using var h1 = await NewAppHost();
        var commander = h1.Services.Commander();
        var session = Session.New();

        // Fill up to the limit
        for (var i = 0; i < SessionTemporalsBackend.MaxEntriesPerSession; i++)
            await commander.Call(new SessionTemporalsBackend_Set(session, $"key-{i}", "v"));

        // One more should fail
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => commander.Call(new SessionTemporalsBackend_Set(session, "overflow", "v")));

        // Updating an existing key should still work
        await commander.Call(new SessionTemporalsBackend_Set(session, "key-0", "updated"));
    }

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
