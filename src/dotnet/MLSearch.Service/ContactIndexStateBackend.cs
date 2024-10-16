using ActualChat.MLSearch.Db;
using ActualChat.Search;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.MLSearch;

public class ContactIndexStateBackend(IServiceProvider services) : DbServiceBase<MLSearchDbContext>(services), IContactIndexStatesBackend
{
    private IDbEntityResolver<string,DbContactIndexState>? _dbContactIndexStateResolver;

    private IDbEntityResolver<string, DbContactIndexState> DbUserContactStateResolver => _dbContactIndexStateResolver ??= Services.GetRequiredService<IDbEntityResolver<string, DbContactIndexState>>();

    // [ComputeMethod]
    public virtual Task<ContactIndexState> GetForUsers(CancellationToken cancellationToken)
        => Get(DbContactIndexState.UserContactIndexStateId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<ContactIndexState> GetForPlaceAuthors(CancellationToken cancellationToken)
        => Get(DbContactIndexState.PlaceAuthorIndexStateId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<ContactIndexState> GetForChats(CancellationToken cancellationToken)
        => Get(DbContactIndexState.ChatContactIndexStateId, cancellationToken);

    // [ComputeMethod]
    public virtual Task<ContactIndexState> GetForPlaces(CancellationToken cancellationToken)
        => Get(DbContactIndexState.PlaceContactIndexStateId, cancellationToken);

    // [CommandHandler]
    public virtual async Task<ContactIndexState> OnChange(
        ContactIndexStatesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sid = id.Value;
        var dbState = await dbContext.ContactIndexStates.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var create)) {
            dbState = new DbContactIndexState(create with { Version = VersionGenerator.NextVersion() });
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

    [ComputeMethod]
    protected virtual async Task<ContactIndexState> Get(Symbol id, CancellationToken cancellationToken)
    {
        var dbState = await DbUserContactStateResolver
            .Get(id.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbState?.ToModel() ?? new ContactIndexState(id);
    }
}
