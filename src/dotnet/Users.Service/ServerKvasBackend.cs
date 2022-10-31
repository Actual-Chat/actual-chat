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

    // [ComputeMethod]
    public virtual async Task<ImmutableList<(string Key, string Value)>> List(string prefix, CancellationToken cancellationToken = default)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbKvasEntryList = await dbContext.KvasEntries
            .Where(e => e.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbKvasEntryList.Select(e => (e.Key[prefix.Length..], e.Value)).ToImmutableList();
    }

    public string GetUserPrefix(string userId)
        => $"u/{userId}/";

    public string GetSessionPrefix(Session session)
        => $"s/{session.Id.Value}/";

    // Command handlers

    // [CommandHandler]
    public virtual async Task SetMany(IServerKvasBackend.SetManyCommand command, CancellationToken cancellationToken = default)
    {
        var prefix = command.Prefix;
        if (Computed.IsInvalidating()) {
            foreach (var (key, _) in command.Items)
                _ = Get(prefix, key, default);
            _ = List(prefix, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
