using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class ServerKvasBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IServerKvasBackend
{
    private IDbEntityResolver<string, DbKvasEntry> DbKvasEntryResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbKvasEntry>>();

    // [ComputeMethod]
    public virtual async Task<byte[]?> Get(string prefix, string key, CancellationToken cancellationToken = default)
    {
        if (prefix.IsNullOrEmpty())
            return null;

        var dbKvasEntry = await DbKvasEntryResolver.Get(prefix + key, cancellationToken).ConfigureAwait(false);
        return dbKvasEntry?.Value;
    }

    // [ComputeMethod]
    public virtual async Task<ApiList<(string Key, byte[] Value)>> List(string prefix, CancellationToken cancellationToken = default)
    {
        if (prefix.IsNullOrEmpty())
            return new ApiList<(string Key, byte[] Value)>();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbKvasEntryList = await dbContext.KvasEntries
            .Where(e => e.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbKvasEntryList.Select(e => (e.Key[prefix.Length..], Value: e.Value)).ToApiList();
    }

    // Command handlers

    // [CommandHandler]
    public virtual async Task OnSetMany(ServerKvasBackend_SetMany command, CancellationToken cancellationToken = default)
    {
        var prefix = command.Prefix;
        if (prefix.IsNullOrEmpty())
            return;

        if (InvalidationMode.IsOn) {
            foreach (var (key, _) in command.Items)
                _ = Get(prefix, key, default);
            _ = List(prefix, default);
            return;
        }

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var keys = command.Items.Select(i => prefix + i.Key).ToHashSet(StringComparer.Ordinal);
        var dbKvasEntryList = await dbContext.KvasEntries.ForUpdate()
            .Where(e => keys.Contains(e.Key))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var dbKvasEntries = dbKvasEntryList.ToDictionary(e => e.Key, StringComparer.Ordinal);

        var items = command.Items.Length <= 1
            ? command.Items
            : command.Items.Reverse().DistinctBy(e => e.Key, StringComparer.Ordinal);
        foreach (var (key, value) in items) {
            var fullKey = prefix + key;
            var dbKvasEntry = dbKvasEntries.GetValueOrDefault(fullKey);
            if (value != null) {
                if (dbKvasEntry == null) {
                    // Create
                    dbKvasEntry = new DbKvasEntry() {
                        Key = fullKey,
                        Version = VersionGenerator.NextVersion(),
                        Value = value,
                    };
                    dbContext.KvasEntries.Add(dbKvasEntry);
                }
                else {
                    // Update
                    dbKvasEntry.Version = VersionGenerator.NextVersion(dbKvasEntry.Version);
                    dbKvasEntry.Value = value;
                    dbContext.KvasEntries.Update(dbKvasEntry);
                }
            }
            else {
                // Remove
                if (dbKvasEntry != null)
                    dbContext.KvasEntries.Remove(dbKvasEntry);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
