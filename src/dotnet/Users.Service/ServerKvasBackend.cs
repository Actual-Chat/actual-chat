using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class ServerKvasBackend : DbServiceBase<UsersDbContext>, IServerKvasBackend
{
    private IDbEntityResolver<string, DbKvasEntry> DbKvasEntryResolver { get; }

    public ServerKvasBackend(IServiceProvider services) : base(services)
        => DbKvasEntryResolver = services.GetRequiredService<IDbEntityResolver<string, DbKvasEntry>>();

    // [ComputeMethod]
    public virtual async Task<string?> Get(string prefix, string key, CancellationToken cancellationToken = default)
    {
        var dbKvasEntry = await DbKvasEntryResolver.Get(prefix + key, cancellationToken).ConfigureAwait(false);
        return dbKvasEntry?.Value;
    }

    // [CommandHandler]
    public virtual async Task Set(IServerKvasBackend.SetManyCommand command, CancellationToken cancellationToken = default)
    {
        var prefix = command.Prefix;
        if (Computed.IsInvalidating()) {
            foreach (var (key, _) in command.Items)
                _ = Get(prefix, key, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var keys = command.Items.Select(i => prefix + i.Key).ToHashSet(StringComparer.Ordinal);
        var dbKvasEntryList = await dbContext.KvasEntries
            .Where(e => keys.Contains(e.Key))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var dbKvasEntries = dbKvasEntryList.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var items = command.Items.Length <= 1
            ? command.Items
            : command.Items.Reverse().DistinctBy(e => e.Key, StringComparer.Ordinal);
        foreach (var (key, value) in items) {
            var dbKvasEntry = dbKvasEntries.GetValueOrDefault(key);
            if (value != null) {
                if (dbKvasEntry == null) {
                    dbKvasEntry = new DbKvasEntry() {
                        Key = key,
                        Version = VersionGenerator.NextVersion(),
                        Value = value,
                    };
                    dbContext.KvasEntries.Add(dbKvasEntry);
                }
                else {
                    dbKvasEntry.Version = VersionGenerator.NextVersion(dbKvasEntry.Version);
                    dbKvasEntry.Value = value;
                    dbContext.KvasEntries.Update(dbKvasEntry);
                }
            }
            else {
                if (dbKvasEntry != null)
                    dbContext.KvasEntries.Remove(dbKvasEntry);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
