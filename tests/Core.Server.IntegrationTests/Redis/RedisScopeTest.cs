using ActualChat.Redis;
using ActualChat.Testing.Host;
using ActualLab.Redis;

namespace ActualChat.Core.Server.IntegrationTests.Redis;

public class RedisScopeTest(ITestOutputHelper @out)
    : LocalAppHostTestBase($"x-{nameof(RedisScopeTest)}", TestAppHostOptions.None, @out)
{
    private RedisDb<InfrastructureDbContext> RedisDb => AppHost.Services.GetRequiredService<RedisDb<InfrastructureDbContext>>();

    [Fact(Timeout = 30_000)]
    public async Task Set_Get_Roundtrip()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());
        var value = new RedisTestValue("k1", 42);

        await scope.Set("k1", value);
        (await scope.Get("k1")).Should().BeEquivalentTo(value);
    }

    [Fact(Timeout = 30_000)]
    public async Task Get_MissingKey_ReturnsDefault()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());
        (await scope.Get("missing")).Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Remove_Works()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());
        await scope.Set("k", new RedisTestValue("k", 1));

        (await scope.Remove("k")).Should().BeTrue();
        (await scope.Get("k")).Should().BeNull();
        (await scope.Remove("k")).Should().BeFalse("second remove should report no existing key");
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_UsesDefaultTtl_WhenTtlIsNull()
    {
        var defaultTtl = TimeSpan.FromMinutes(5);
        var prefix = NewPrefix();
        var scope = new RedisScope<RedisTestValue>(RedisDb, prefix) { DefaultTtl = defaultTtl };

        await scope.Set("k", new RedisTestValue("k", 1));

        var ttl = await ReadTtl(prefix, "k");
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4))
            .And.BeLessThanOrEqualTo(defaultTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_ExplicitTtl_OverridesDefault()
    {
        var prefix = NewPrefix();
        var scope = new RedisScope<RedisTestValue>(RedisDb, prefix) {
            DefaultTtl = TimeSpan.FromMinutes(5),
        };
        var explicitTtl = TimeSpan.FromSeconds(30);

        await scope.Set("k", new RedisTestValue("k", 1), explicitTtl);

        var ttl = await ReadTtl(prefix, "k");
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(5))
            .And.BeLessThanOrEqualTo(explicitTtl);
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_NoDefaultTtl_AndNullTtl_PersistsForever()
    {
        var prefix = NewPrefix();
        var scope = new RedisScope<RedisTestValue>(RedisDb, prefix);

        await scope.Set("k", new RedisTestValue("k", 1));

        var ttl = await ReadTtl(prefix, "k");
        ttl.Should().BeNull("null default TTL + null ttl argument must mean no expiration");
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_ShortTtl_KeyExpires()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());

        await scope.Set("k", new RedisTestValue("k", 1), TimeSpan.FromMilliseconds(500));
        (await scope.Get("k")).Should().NotBeNull();

        await Task.Delay(TimeSpan.FromSeconds(2));
        (await scope.Get("k")).Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_EmptyKeyPrefix_Works()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, "");
        var key = $"redis-scope-test:empty-prefix:{Guid.NewGuid():N}";

        try {
            await scope.Set(key, new RedisTestValue("v", 1));
            (await scope.Get(key))!.Number.Should().Be(1);
        }
        finally {
            await scope.Remove(key);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Set_NullKeyPrefix_Works()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, keyPrefix: null);
        var key = $"redis-scope-test:null-prefix:{Guid.NewGuid():N}";

        try {
            await scope.Set(key, new RedisTestValue("v", 2));
            (await scope.Get(key))!.Number.Should().Be(2);
        }
        finally {
            await scope.Remove(key);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task ListKeys_ReturnsAllKeys_AndIsolatedByPrefix()
    {
        var prefixA = NewPrefix();
        var prefixB = NewPrefix();
        var scopeA = new RedisScope<RedisTestValue>(RedisDb, prefixA);
        var scopeB = new RedisScope<RedisTestValue>(RedisDb, prefixB);

        await scopeA.Set("a1", new RedisTestValue("a1", 1));
        await scopeA.Set("a2", new RedisTestValue("a2", 2));
        await scopeA.Set("a3", new RedisTestValue("a3", 3), TimeSpan.FromMinutes(5));
        await scopeB.Set("b1", new RedisTestValue("b1", 1));

        (await scopeA.ListKeys().ToListAsync()).Should().BeEquivalentTo(["a1", "a2", "a3"]);
        (await scopeB.ListKeys().ToListAsync()).Should().BeEquivalentTo(["b1"]);
    }

    [Fact(Timeout = 30_000)]
    public async Task ListKeys_AppliesKeyPattern()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());
        await scope.Set("user:1", new RedisTestValue("u1", 1));
        await scope.Set("user:2", new RedisTestValue("u2", 2));
        await scope.Set("session:1", new RedisTestValue("s1", 1));

        (await scope.ListKeys("user:*").ToListAsync()).Should().BeEquivalentTo(["user:1", "user:2"]);
        (await scope.ListKeys("session:*").ToListAsync()).Should().BeEquivalentTo(["session:1"]);
    }

    [Fact(Timeout = 30_000)]
    public async Task ListKeys_OnEmptyMap_ReturnsEmpty()
    {
        var scope = new RedisScope<RedisTestValue>(RedisDb, NewPrefix());
        (await scope.ListKeys().ToListAsync()).Should().BeEmpty();
    }

    // --- Helpers ---

    private static string NewPrefix()
        => $"redis-scope-test:{Guid.NewGuid():N}";

    private async Task<TimeSpan?> ReadTtl(string keyPrefix, string key)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var fullKey = $"{keyPrefix}{RedisDb.KeyDelimiter}{key}";
        return await db.KeyTimeToLiveAsync(fullKey).ConfigureAwait(false);
    }
}
