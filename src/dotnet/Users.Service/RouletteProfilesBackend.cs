using System.Linq.Expressions;
using ActualChat.Roulette;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class RouletteProfilesBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IRouletteProfilesBackend
{
    private IAvatarsBackend AvatarsBackend { get; } = services.GetRequiredService<IAvatarsBackend>();

    private IDbEntityResolver<string, DbRouletteProfilePrefs> RouletteProfilePrefsResolver { get; }
            = services.GetRequiredService<IDbEntityResolver<string, DbRouletteProfilePrefs>>();

    // [ComputeMethod]
    public virtual async Task<ProfileFull?> GetProfile(Symbol profileId, CancellationToken cancellationToken)
    {
        if (profileId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId));

        var avatar = await AvatarsBackend.Get(profileId, cancellationToken).ConfigureAwait(false);
        if (avatar == null)
            return null;

        var prefs = await GetPreferences(profileId, cancellationToken).ConfigureAwait(false);
        return new ProfileFull(avatar.UserId, profileId) {
            Avatar = avatar.ToAvatar(),
            Preferences = prefs ?? new ProfilePreferences(profileId)
        };
    }

    public virtual async Task<ImmutableArray<ProfilePreferences>> FindProfiles(
        Preferences filter,
        CancellationToken cancellationToken)
    {
        if (filter.Languages.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(filter));

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        IQueryable<DbRouletteProfilePrefs> queryable = dbContext.RouletteProfilePrefs;
        if (!filter.Country.IsNotSpecified)
            queryable = queryable.Where(c => c.CountryCode == filter.Country.Code);
        if (filter.Gender != Gender.NotSpecified)
            queryable = queryable.Where(c => c.Gender == filter.Gender);
        var filterLanguageIds = filter.Languages.Select(l => l.Id.Value).ToArray();
        queryable = queryable.Where(c => filterLanguageIds.Any(l => c.Languages.Contains(l)));
        if (filter.Interests.Length > 0) {
            var filterInterestCodes = filter.Interests.Select(c => c.Code).ToArray();
            queryable = queryable.Where(c => filterInterestCodes.Any(i => c.Interests.Contains(i)));
        }
        var candidates = await queryable.ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return candidates.Select(c => c.ToModel()).ToImmutableArray();
    }

    [ComputeMethod]
    protected virtual async Task<ProfilePreferences?> GetPreferences(Symbol profileId, CancellationToken cancellationToken)
    {
        if (profileId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId));

        var dbProfilePrefs = await RouletteProfilePrefsResolver.Get(profileId, cancellationToken).ConfigureAwait(false);
        return dbProfilePrefs?.ToModel();
    }

    // Commands

    public virtual async Task<ProfilePreferences?> OnChangePrefs(
        RouletteProfilesBackend_ChangePrefs command,
        CancellationToken cancellationToken)
    {
        var (profileId, expectedVersion, change) = command;
        profileId.RequireNonEmpty(nameof(RouletteProfilesBackend_ChangePrefs.ProfileId));

        if (Invalidation.IsActive) {
            _ = GetPreferences(profileId, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        ProfilePreferences? profilePrefs;
        if (change.IsCreate(out var profile)) {
            if (!profile.Id.IsEmpty && !profile.Id.Equals(profileId))
                throw StandardError.Constraint("Change profile id should be empty or match command profile id.");

            profile = profile with {
                Id = profileId,
                Version = VersionGenerator.NextVersion(),
            };
            var dbPrefs = new DbRouletteProfilePrefs(profile);
            dbContext.RouletteProfilePrefs.Add(dbPrefs);
            profilePrefs = dbPrefs.ToModel();
        }
        else {
            var dbPrefs = await dbContext.RouletteProfilePrefs
                .Get(profileId, cancellationToken)
                .ConfigureAwait(false);

            if (change.IsUpdate(out profile)) {
                dbPrefs.RequireVersion(expectedVersion);
                profile = profile with {
                    Version = VersionGenerator.NextVersion(profile.Version),
                };
                dbPrefs.UpdateFrom(profile);
            }
            else {
                if (expectedVersion is not null)
                    dbPrefs.RequireVersion(expectedVersion);
                if (dbPrefs is not null)
                    dbContext.Remove(dbPrefs);
            }

            profilePrefs = dbPrefs?.ToModel();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return profilePrefs;
    }

    // [EventHandler]
    public virtual Task OnAvatarChangedEvent(AvatarChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask;

        var (avatar, _, change) = eventCommand;
        if (change != ChangeKind.Remove)
            return Task.CompletedTask;

        var profileId = avatar.Id;
        var removePrefs = new RouletteProfilesBackend_ChangePrefs(profileId, null, Change.Remove<ProfilePreferences>());
        return Commander.Call(removePrefs, true, cancellationToken);
    }
}
