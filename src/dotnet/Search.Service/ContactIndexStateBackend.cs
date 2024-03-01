using ActualChat.Search.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Search;

public class ContactIndexStateBackend(IServiceProvider services) : DbServiceBase<SearchDbContext>(services), IContactIndexStatesBackend
{
    private IDbEntityResolver<string,DbContactIndexState>? _dbContactIndexStateResolver;

    private IDbEntityResolver<string, DbContactIndexState> DbUserContactStateResolver => _dbContactIndexStateResolver ??= Services.GetRequiredService<IDbEntityResolver<string, DbContactIndexState>>();

    public virtual async Task<ContactIndexState> GetForUsers(CancellationToken cancellationToken)
    {
        var id = DbContactIndexState.UserContactIndexStateId;
        var state = await Get(id, cancellationToken).ConfigureAwait(false);
        return state ?? new ContactIndexState(id);
    }

    public virtual async Task<ContactIndexState> GetForChats(CancellationToken cancellationToken)
    {
        var id = DbContactIndexState.ChatContactIndexStateId;
        var state = await Get(id, cancellationToken).ConfigureAwait(false);
        return state ?? new ContactIndexState(id);
    }

    public virtual async Task<ContactIndexState> OnChange(
        ContactIndexStatesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            _ = GetForUsers(default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sid = id.Value;
        var dbState = await dbContext.ContactIndexStates.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var create)) {
            dbState = new DbContactIndexState {
                Id = create.Id,
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(dbState);
        }
        else if (change.IsUpdate(out var update)) {
            dbState.Require();
            dbState.RequireVersion(expectedVersion);
            update = update with { Version = VersionGenerator.NextVersion(dbState.Version) };
            dbState.UpdateFrom(update);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbState!.ToModel();
    }

    // Private methods

    private async Task<ContactIndexState?> Get(string id, CancellationToken cancellationToken)
    {
        var dbState = await DbUserContactStateResolver
            .Get(id, cancellationToken)
            .ConfigureAwait(false);
        return dbState?.ToModel();
    }
}
