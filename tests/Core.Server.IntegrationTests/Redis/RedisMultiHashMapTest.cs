using ActualChat.Redis;
using ActualChat.Testing.Host;
using ActualLab.Redis;

namespace ActualChat.Core.Server.IntegrationTests.Redis;

public class RedisMultiHashMapTest(ITestOutputHelper @out)
    : LocalAppHostTestBase($"x-{nameof(RedisMultiHashMapTest)}", TestAppHostOptions.None, @out)
{
    private RedisDb<InfrastructureDbContext> RedisDb => AppHost.Services.GetRequiredService<RedisDb<InfrastructureDbContext>>();

    [Fact(Timeout = 30_000)]
    public async Task Set_GetAll_Roundtrip()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());
        var hashKey = "h1";

        await hash.Set(hashKey, "f1", new RedisTestValue("f1", 1));
        await hash.Set(hashKey, "f2", new RedisTestValue("f2", 2));

        var all = await hash.GetHashMap(hashKey);
        all.Should().HaveCount(2);
        all["f1"]!.Number.Should().Be(1);
        all["f2"]!.Number.Should().Be(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task Remove_Works()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());
        var hashKey = "h";

        await hash.Set(hashKey, "a", new RedisTestValue("a", 1));
        await hash.Set(hashKey, "b", new RedisTestValue("b", 2));

        (await hash.Remove(hashKey, "a")).Should().BeTrue();
        (await hash.Remove(hashKey, "a")).Should().BeFalse("second remove should find nothing");

        var all = await hash.GetHashMap(hashKey);
        all.Should().ContainKey("b").And.NotContainKey("a");
    }

    [Fact(Timeout = 30_000)]
    public async Task DeleteKey_RemovesWholeHash()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());
        var hashKey = "h";

        await hash.Set(hashKey, "a", new RedisTestValue("a", 1));
        await hash.Set(hashKey, "b", new RedisTestValue("b", 2));

        await hash.RemoveHashMap(hashKey);
        (await hash.GetHashMap(hashKey)).Should().BeEmpty();
    }

    [Fact(Timeout = 30_000)]
    public async Task DifferentHashKeys_AreIsolated()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());

        await hash.Set("h1", "f", new RedisTestValue("h1", 1));
        await hash.Set("h2", "f", new RedisTestValue("h2", 2));

        (await hash.GetHashMap("h1"))["f"]!.Number.Should().Be(1);
        (await hash.GetHashMap("h2"))["f"]!.Number.Should().Be(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_WithoutTtls_NoExpiration()
    {
        var prefix = NewPrefix();
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix);
        var hashKey = "h";

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1));

        var keyTtl = await ReadHashKeyTtl(prefix, hashKey);
        keyTtl.Should().BeNull("no HashTtl configured");

        var fieldTtl = await ReadHashFieldTtl(prefix, hashKey, "f");
        fieldTtl.Should().Be(TimeSpan.Zero, "no field TTL configured");
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_HashTtl_RefreshesKeyExpiration()
    {
        var prefix = NewPrefix();
        var hashTtl = TimeSpan.FromMinutes(5);
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix) { HashTtl = hashTtl };
        var hashKey = "h";

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1));

        var keyTtl = await ReadHashKeyTtl(prefix, hashKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4))
            .And.BeLessThanOrEqualTo(hashTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_DefaultFieldTtl_AppliesHexpire()
    {
        var prefix = NewPrefix();
        var defaultFieldTtl = TimeSpan.FromMinutes(5);
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix) { DefaultFieldTtl = defaultFieldTtl };
        var hashKey = "h";

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1));

        var fieldTtl = await ReadHashFieldTtl(prefix, hashKey, "f");
        fieldTtl.Should().BeGreaterThan(TimeSpan.FromMinutes(4))
            .And.BeLessThanOrEqualTo(defaultFieldTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_ExplicitFieldTtl_OverridesDefault()
    {
        var prefix = NewPrefix();
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix) {
            DefaultFieldTtl = TimeSpan.FromMinutes(5),
        };
        var hashKey = "h";
        var explicitTtl = TimeSpan.FromSeconds(30);

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1), explicitTtl);

        var fieldTtl = await ReadHashFieldTtl(prefix, hashKey, "f");
        fieldTtl.Should().BeGreaterThan(TimeSpan.FromSeconds(5))
            .And.BeLessThanOrEqualTo(explicitTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_BothTtls_AreApplied()
    {
        var prefix = NewPrefix();
        var hashTtl = TimeSpan.FromMinutes(10);
        var fieldTtl = TimeSpan.FromMinutes(5);
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix) {
            HashTtl = hashTtl,
            DefaultFieldTtl = fieldTtl,
        };
        var hashKey = "h";

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1));

        var keyTtl = await ReadHashKeyTtl(prefix, hashKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(9))
            .And.BeLessThanOrEqualTo(hashTtl);

        var fTtl = await ReadHashFieldTtl(prefix, hashKey, "f");
        fTtl.Should().BeGreaterThan(TimeSpan.FromMinutes(4))
            .And.BeLessThanOrEqualTo(fieldTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Remove_RefreshesHashTtl_WhenSet()
    {
        var prefix = NewPrefix();
        var hashTtl = TimeSpan.FromMinutes(5);
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, prefix) { HashTtl = hashTtl };
        var hashKey = "h";

        await hash.Set(hashKey, "a", new RedisTestValue("a", 1));
        await hash.Set(hashKey, "b", new RedisTestValue("b", 2));
        await hash.Remove(hashKey, "a");

        var keyTtl = await ReadHashKeyTtl(prefix, hashKey);
        keyTtl.Should().NotBeNull();
        keyTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4))
            .And.BeLessThanOrEqualTo(hashTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_ShortFieldTtl_FieldExpires()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());
        var hashKey = "h";

        await hash.Set(hashKey, "f", new RedisTestValue("f", 1), TimeSpan.FromMilliseconds(500));
        (await hash.GetHashMap(hashKey)).Should().ContainKey("f");

        await Task.Delay(TimeSpan.FromSeconds(2));
        (await hash.GetHashMap(hashKey)).Should().NotContainKey("f");
    }

    [Fact(Timeout = 30_000)]
    public async Task EmptyKeyPrefix_Works()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, "");
        var hashKey = $"redis-multihash-test:empty-prefix:{Guid.NewGuid():N}";

        try {
            await hash.Set(hashKey, "f", new RedisTestValue("f", 1));
            (await hash.GetHashMap(hashKey))["f"]!.Number.Should().Be(1);
            (await hash.Get(hashKey, "f"))!.Number.Should().Be(1);
        }
        finally {
            await hash.RemoveHashMap(hashKey);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Get_ReturnsField_OrNullWhenMissing()
    {
        var hash = new RedisMultiHashMap<RedisTestValue>(RedisDb, NewPrefix());
        var hashKey = "h";

        await hash.Set(hashKey, "f1", new RedisTestValue("f1", 7));
        (await hash.Get(hashKey, "f1"))!.Number.Should().Be(7);
        (await hash.Get(hashKey, "missing")).Should().BeNull();
        (await hash.Get("missing-hash", "f1")).Should().BeNull();
    }

    // --- Helpers ---

    private static string NewPrefix()
        => $"redis-multihash-test:{Guid.NewGuid():N}";

    private async Task<TimeSpan?> ReadHashKeyTtl(string keyPrefix, string hashKey)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var fullKey = $"{keyPrefix}{RedisDb.KeyDelimiter}{hashKey}";
        return await db.KeyTimeToLiveAsync(fullKey).ConfigureAwait(false);
    }

    // Returns remaining TTL as TimeSpan; TimeSpan.Zero if field has no TTL or doesn't exist.
    private async Task<TimeSpan> ReadHashFieldTtl(string keyPrefix, string hashKey, string field)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var fullKey = $"{keyPrefix}{RedisDb.KeyDelimiter}{hashKey}";
        var ttls = await db.HashFieldGetTimeToLiveAsync(fullKey, [field]).ConfigureAwait(false);
        var ms = ttls[0];
        // HPTTL: -1 = persistent, -2 = field missing, otherwise remaining ms
        return ms < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms);
    }
}
