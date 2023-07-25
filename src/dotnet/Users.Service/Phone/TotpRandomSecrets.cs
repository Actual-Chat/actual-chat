using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Stl.Generators;
using Stl.Redis;

namespace ActualChat.Users;

public class TotpRandomSecrets
{
    private const string RedisKeyPrefix = ".TotpRandomSecret.";
    private readonly RandomStringGenerator _rsg;

    private RedisDb<UsersDbContext> RedisDb { get; }
    private UsersSettings Settings { get; }
    private IDataProtector DataProtector { get; }

    public TotpRandomSecrets(IServiceProvider services)
    {
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
        DataProtector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(TotpRandomSecrets));
        Settings = services.GetRequiredService<UsersSettings>();

        _rsg = new (Settings.TotpRandomSecretLength);
    }

    [ComputeMethod]
    public virtual async Task<string> Get(Session session)
        => await GetOrSet(ToKey(session), _rsg.Next()).ConfigureAwait(false);

    private static string ToKey(Session session)
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
}
