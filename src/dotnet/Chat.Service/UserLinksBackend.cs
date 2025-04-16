using ActualChat.Chat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class UserLinksBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IUserLinksBackend
{
    private IDbEntityResolver<string, DbUserLink> DbUserLinkResolver { get; } = services.GetRequiredService<IDbEntityResolver<string, DbUserLink>>();

    public virtual async Task<UserLink?> Get(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        var dbUserLink = await DbUserLinkResolver.Get(userLinkId.NormalizedValue, cancellationToken).ConfigureAwait(false);
        return dbUserLink?.ToModel();
    }

    public virtual async Task<UserLink?> OnChange(UserLinksBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;

        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return default!;
        }

        id.Require();
        change.RequireValid();
        var sid = id.NormalizedValue;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserLink = await dbContext.UserLinks.ForUpdate()
            .FirstOrDefaultAsync(c => c.Id == sid, cancellationToken)
            .ConfigureAwait(false);

        if (change.IsCreate(out var userLink)) {
            if (dbUserLink != null)
                throw StandardError.Constraint($"UserLink with id '{sid}' already exists.");

            // Checks

            if (!userLink.Id.Equals(id))
                throw StandardError.Constraint($"UserLink.Id should match command.UserLinkId.");

            if (userLink.TargetId.IsNullOrEmpty())
                throw StandardError.Constraint($"UserLink.TargetId should not be empty.");

            userLink = userLink with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbUserLink = new DbUserLink(userLink);
            dbContext.Add(dbUserLink);
        }
        else if (change.IsUpdate(out _))
            throw StandardError.NotSupported("Update change is not allowed.");
        else { // Remove
            if (expectedVersion != null)
                dbUserLink.RequireVersion(expectedVersion);
            if (dbUserLink == null)
                return null;

            dbContext.Remove(dbUserLink);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        userLink = dbUserLink.ToModel();
        return userLink;
    }
}
