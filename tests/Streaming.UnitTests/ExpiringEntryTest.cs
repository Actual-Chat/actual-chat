namespace ActualChat.Streaming.UnitTests;

public class ExpiringEntryTest
{
    [Fact(Timeout = 10_000)]
    public async Task ShouldExpireAfterTtl()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(200));
        entry.BeginExpire();

        dict.Should().ContainKey("key1");
        await Task.Delay(500);
        dict.Should().NotContainKey("key1", "entry should be removed after TTL");
        entry.IsDisposed.Should().BeTrue();
    }

    [Fact(Timeout = 10_000)]
    public async Task BumpExpiresAt_ShouldExtendLifetime()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(300));
        entry.BeginExpire();

        // After 200ms, bump again
        await Task.Delay(200);
        dict.Should().ContainKey("key1", "entry should still be alive before TTL");
        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(500));

        // After another 200ms (total 400ms from start, but only 200ms from bump)
        await Task.Delay(200);
        dict.Should().ContainKey("key1", "entry should still be alive after bump");

        // Wait for the extended TTL to expire
        await Task.Delay(500);
        dict.Should().NotContainKey("key1", "entry should be removed after extended TTL");
    }

    [Fact(Timeout = 10_000)]
    public async Task Disposer_ShouldBeCalledOnExpiration()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var disposerCalled = false;
        string? disposedKey = null;

        var entry = ExpiringEntry.New(dict, "key1", "value1");
        entry.SetDisposer(e => {
            disposerCalled = true;
            disposedKey = e.Key;
        });
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(200));
        entry.BeginExpire();

        await Task.Delay(500);
        disposerCalled.Should().BeTrue("disposer should be called on expiration");
        disposedKey.Should().Be("key1");
    }

    [Fact(Timeout = 10_000)]
    public async Task ShouldNotExpireWhileBeingBumped()
    {
        var dict = new ConcurrentDictionary<string, ExpiringEntry<string, string>>();
        var entry = ExpiringEntry.New(dict, "key1", "value1");
        dict["key1"] = entry;

        entry.BumpExpiresAt(TimeSpan.FromMilliseconds(500));
        entry.BeginExpire();

        // Bump every 200ms for 1 second — entry should survive
        for (var i = 0; i < 5; i++) {
            await Task.Delay(200);
            entry.BumpExpiresAt(TimeSpan.FromMilliseconds(500));
        }

        dict.Should().ContainKey("key1", "entry should still be alive while being bumped");

        // Now stop bumping and wait for expiration
        await Task.Delay(800);
        dict.Should().NotContainKey("key1", "entry should expire after bumping stops");
    }
}
