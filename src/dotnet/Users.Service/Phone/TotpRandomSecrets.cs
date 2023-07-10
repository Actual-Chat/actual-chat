using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Stl.Generators;
using Stl.Redis;

namespace ActualChat.Users;

public class TotpRandomSecrets : IComputeService
{
    private const string RedisKeyPrefix = ".TotpRandomSecret.";
    private readonly RandomStringGenerator _rng;

    private RedisDb<UsersDbContext> RedisDb { get; }
    private UsersSettings Settings { get; }
    private IDataProtector DataProtector { get; }

    public TotpRandomSecrets(IServiceProvider services)
    {
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
        DataProtector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(TotpRandomSecrets));
        Settings = services.GetRequiredService<UsersSettings>();

        _rng = new (Settings.TotpRandomSecretLength);
    }

    [ComputeMethod]
    public virtual async Task<string> Get(Session session)
    {
        var key = Key(session);
        var secret = await GetOrSet(key, _rng.Next()).ConfigureAwait(false);
        var expiresAt = await GetExpiration(key).ConfigureAwait(false);
        var computed = Computed.GetCurrent();
        // make sure security token doesn't change before totp is entered
        computed!.Invalidate(expiresAt - Settings.TotpLifetime - TimeSpan.FromSeconds(5));
        return secret;
    }

    private string Key(Session session)
        => $"{RedisKeyPrefix}{session.Id}";

    // redis helpers

    private async Task<string> GetOrSet(string key, string value)
    {
        var protectedValue = DataProtector.Protect(value);
        // avoiding multiple sets from different replicas
        // TODO: use StringSetAndGetAsync after switching to redis 7.0
        var wasUpdated = await RedisDb.Database.StringSetAsync(key,
                protectedValue,
                Settings.TotpLifetime * 2,
                false,
                When.NotExists)
            .ConfigureAwait(false);
        if (wasUpdated)
            return value;

        var existing = await RedisDb.Database.StringGetAsync(key).ConfigureAwait(false);
        return DataProtector.Unprotect(existing.ToString());
    }

    private async Task<TimeSpan> GetExpiration(string key)
    {
        var expiresAt = await RedisDb.Database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
        return expiresAt ?? TimeSpan.Zero;
    }
}
