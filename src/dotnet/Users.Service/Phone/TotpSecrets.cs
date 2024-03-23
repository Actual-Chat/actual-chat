using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using ActualLab.Generators;
using ActualLab.Redis;

namespace ActualChat.Users;

public sealed class TotpSecrets
{
    private const string RedisKeyPrefix = ".TotpRandomSecret.";
    private readonly RandomStringGenerator _rsg;

    private RedisDb<UsersDbContext> RedisDb { get; }
    private UsersSettings Settings { get; }
    private IDataProtector DataProtector { get; }

    public TotpSecrets(IServiceProvider services)
    {
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
        DataProtector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(TotpSecrets));
        Settings = services.GetRequiredService<UsersSettings>();

        _rsg = new(Settings.TotpRandomSecretLength);
    }

    public Task<string> Get(Session session, CancellationToken cancellationToken = default)
        => GetOrSet(ToKey(session), _rsg.Next(), cancellationToken);

    // Private methods

    private static string ToKey(Session session)
        => $"{RedisKeyPrefix}{session.Id}";

    private async Task<string> GetOrSet(string key, string value, CancellationToken cancellationToken)
    {
        var protectedValue = DataProtector.Protect(value);

        // Avoiding multiple sets from different replicas
        // TODO: use StringSetAndGetAsync after switching to redis 7.0
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var wasUpdated = await database.StringSetAsync(key,
                protectedValue,
                Settings.TotpLifetime * 2,
                false,
                When.NotExists)
            .ConfigureAwait(false);
        if (wasUpdated)
            return value;

        var existing = await database.StringGetAsync(key).ConfigureAwait(false);
        return DataProtector.Unprotect(existing.ToString());
    }
}
