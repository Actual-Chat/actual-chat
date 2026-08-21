using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class SessionTemporalsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task BasicTest()
    {
        var sessionTemporals = AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session = Session.New();
        var key = "test-key";

        // Initially empty
        var value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().BeNull();

        // Set a value
        await Commander.Call(new SessionTemporalsBackend_Set(session, key, "hello"));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().Be("hello");

        // Overwrite
        await Commander.Call(new SessionTemporalsBackend_Set(session, key, "world"));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().Be("world");

        // Remove
        await Commander.Call(new SessionTemporalsBackend_Set(session, key, null));
        value = await sessionTemporals.Get(session, key, CancellationToken.None);
        value.Should().BeNull();
    }

    [Fact]
    public async Task MultipleKeysTest()
    {
        var sessionTemporals = AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session = Session.New();

        await Commander.Call(new SessionTemporalsBackend_Set(session, "a", "1"));
        await Commander.Call(new SessionTemporalsBackend_Set(session, "b", "2"));
        await Commander.Call(new SessionTemporalsBackend_Set(session, "c", "3"));

        (await sessionTemporals.Get(session, "a", default)).Should().Be("1");
        (await sessionTemporals.Get(session, "b", default)).Should().Be("2");
        (await sessionTemporals.Get(session, "c", default)).Should().Be("3");

        // Remove one, others stay
        await Commander.Call(new SessionTemporalsBackend_Set(session, "b", null));
        (await sessionTemporals.Get(session, "a", default)).Should().Be("1");
        (await sessionTemporals.Get(session, "b", default)).Should().BeNull();
        (await sessionTemporals.Get(session, "c", default)).Should().Be("3");
    }

    [Fact]
    public async Task SessionIsolationTest()
    {
        var sessionTemporals = AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session1 = Session.New();
        var session2 = Session.New();

        await Commander.Call(new SessionTemporalsBackend_Set(session1, "key", "value1"));
        await Commander.Call(new SessionTemporalsBackend_Set(session2, "key", "value2"));

        (await sessionTemporals.Get(session1, "key", default)).Should().Be("value1");
        (await sessionTemporals.Get(session2, "key", default)).Should().Be("value2");
    }

    [Fact]
    public async Task ClientKeyCannotCollideWithServerKey()
    {
        // arrange
        var backend = AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session = Session.New();
        var serverKey = Constants.SessionTemporals.PendingRegistrationKey;
        await Commander.Call(new SessionTemporalsBackend_Set(session, serverKey, "server-value"));

        // act
        await Commander.Call(new SessionTemporals_Set { Session = session, Key = serverKey, Value = "client-value" });

        // assert
        (await backend.Get(session, serverKey, default)).Should().Be("server-value");
    }

    [Fact]
    public async Task ClientKeysRoundTripAndServerKeysStayReadable()
    {
        // arrange
        var sessionTemporals = AppHost.Services.GetRequiredService<ISessionTemporals>();
        var backend = AppHost.Services.GetRequiredService<ISessionTemporalsBackend>();
        var session = Session.New();
        var signInErrorKey = Constants.SessionTemporals.SignInErrorKey;
        await Commander.Call(new SessionTemporalsBackend_Set(session, signInErrorKey, "boom"));

        // act
        await Commander.Call(new SessionTemporals_Set { Session = session, Key = "own-key", Value = "own-value" });
        var ownValue = await sessionTemporals.Get(session, "own-key", default);
        var signInError = await sessionTemporals.Get(session, signInErrorKey, default);
        await Commander.Call(new SessionTemporals_Set { Session = session, Key = signInErrorKey, Value = null });

        // assert
        ownValue.Should().Be("own-value");
        signInError.Should().Be("boom");
        (await backend.Get(session, signInErrorKey, default)).Should().BeNull();
    }

    [Fact]
    public async Task KeyLengthLimitTest()
    {
        var session = Session.New();

        var longKey = new string('k', SessionTemporalsBackend.MaxKeyLength + 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Commander.Call(new SessionTemporalsBackend_Set(session, longKey, "v")));

        // Exactly at limit should work
        var maxKey = new string('k', SessionTemporalsBackend.MaxKeyLength);
        await Commander.Call(new SessionTemporalsBackend_Set(session, maxKey, "v"));
    }

    [Fact]
    public async Task ValueLengthLimitTest()
    {
        var session = Session.New();

        var longValue = new string('v', SessionTemporalsBackend.MaxValueLength + 1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Commander.Call(new SessionTemporalsBackend_Set(session, "key", longValue)));

        // Exactly at limit should work
        var maxValue = new string('v', SessionTemporalsBackend.MaxValueLength);
        await Commander.Call(new SessionTemporalsBackend_Set(session, "key", maxValue));
    }

    [Fact]
    public async Task EntryCountLimitTest()
    {
        var session = Session.New();

        // Fill up to the limit
        for (var i = 0; i < SessionTemporalsBackend.MaxEntriesPerSession; i++)
            await Commander.Call(new SessionTemporalsBackend_Set(session, $"key-{i}", "v"));

        // One more should fail
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Commander.Call(new SessionTemporalsBackend_Set(session, "overflow", "v")));

        // Updating an existing key should still work
        await Commander.Call(new SessionTemporalsBackend_Set(session, "key-0", "updated"));
    }
}
