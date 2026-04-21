namespace ActualChat.Streaming.UnitTests;

public class ExpiringEntryTest
{
    [Fact(Timeout = 10_000)]
    public async Task ShouldExpireAfterTtl()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(1000));
        entry.BeginExpire();

        dict.Should().ContainKey("key1");
        await Task.Delay(2000);
        dict.Should().NotContainKey("key1", "entry should be removed after TTL");
        entry.IsDisposed.Should().BeTrue();
    }

    [Fact(Timeout = 10_000)]
    public async Task BumpExpiresAt_ShouldExtendLifetime()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(1500));
        entry.BeginExpire();

        // After 500ms, bump again
        await Task.Delay(500);
        dict.Should().ContainKey("key1", "entry should still be alive before TTL");
        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(1500));

        // After another 500ms (total 1000ms from start, but only 500ms from bump)
        await Task.Delay(500);
        dict.Should().ContainKey("key1", "entry should still be alive after bump");

        // Wait for the extended TTL to expire
        await Task.Delay(2500);
        dict.Should().NotContainKey("key1", "entry should be removed after extended TTL");
    }

    [Fact(Timeout = 10_000)]
    public async Task Disposer_ShouldBeCalledOnExpiration()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        // Wait on a TCS instead of a fixed Task.Delay — under ThreadPool pressure the
        // BackgroundTask scheduled by BeginExpire can overshoot its 1000ms target, which
        // made the earlier `await Task.Delay(2000); disposerCalled.Should().BeTrue()` flaky.
        var disposedKeyTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = ExpiringEntry.New(dict, "key1", "value1");
        entry.SetDisposer(e => disposedKeyTcs.TrySetResult(e.Key));
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(1000));
        entry.BeginExpire();

        var disposedKey = await disposedKeyTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        disposedKey.Should().Be("key1");
    }

    [Fact(Timeout = 10_000)]
    public async Task ShouldNotExpireWhileBeingBumped()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(2000));
        entry.BeginExpire();

        // Bump every 500ms for 2.5 seconds — entry should survive
        for (var i = 0; i < 5; i++) {
            await Task.Delay(500);
            entry.BumpExpiresAt(TimeSpan.FromMilliseconds(2000));
        }

        dict.Should().ContainKey("key1", "entry should still be alive while being bumped");

        // Now stop bumping and wait for expiration
        await Task.Delay(3000);
        dict.Should().NotContainKey("key1", "entry should expire after bumping stops");
    }
}
