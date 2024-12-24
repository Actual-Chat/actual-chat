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

    private IDbEntityResolver<string, DbRouletteUserSettings> RouletteUserSettingsResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbRouletteUserSettings>>();

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

    public virtual async Task<RouletteUserSettings?> GetUserSettings(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userId));

        var dbRouletteUserSettings = await RouletteUserSettingsResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return dbRouletteUserSettings?.ToModel();
    }

    public virtual async Task<ImmutableArray<ProfilePreferences>> FindProfiles(
        UserId ownUserId,
        Symbol ownProfileId,
        Preferences filter,
        CancellationToken cancellationToken)
    {
        if (filter.Languages.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(filter));

        var ownUserSid = ownUserId.Value;
        var ownProfileSid = ownProfileId.Value;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var completionIdPrefix = ownUserSid + ":";
        var completions = dbContext.RouletteCompletions
            .Where(c => c.Id.StartsWith(completionIdPrefix));
        var completedPeerProfileIds = completions
            .Where(c => c.OwnerProfileId == ownProfileSid)
            .Select(c => c.PeerProfileId);

        IQueryable<DbRouletteProfilePrefs> queryable = dbContext.RouletteProfilePrefs;
        queryable = queryable
            .Where(c => !completedPeerProfileIds.Contains(c.Id)); // Exclude completed chats
        // NOTE: When should we exclude profiles by user id?
        if (!filter.Country.IsNotSpecified)
            queryable = queryable.Where(c => c.CountryCode == filter.Country.Code);
        if (filter.Gender != Gender.NotSpecified)
            queryable = queryable.Where(c => c.Gender == filter.Gender);
        var filterLanguageIds = filter.Languages.Select(l => l.Id.Value).ToArray();
        queryable = queryable.Where(c => filterLanguageIds.Any(l => c.Languages.Contains(l)));
        if (filter.Interests.Length > 0) {
            var hasFlexible = filter.Interests.Any(c => c.Code == Interests.Flexible.Code);
            if (hasFlexible) {
                // Flexible interest indicates that profiles with any interests will suite.
                queryable = queryable.Where(c => c.Interests.Length > 0);
            }
            else {
                var filterInterestCodes = filter.Interests.Select(c => c.Code).ToArray();
                queryable = queryable.Where(c => filterInterestCodes.Any(i => c.Interests.Contains(i)));
            }
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

    public virtual async Task<RouletteUserSettings?> OnChangeUserSettings(
        RouletteProfilesBackend_ChangeUserSettings command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        id.Require(nameof(RouletteProfilesBackend_ChangeUserSettings.Id));

        if (Invalidation.IsActive) {
            _ = GetUserSettings(id, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        RouletteUserSettings? userSettings = null;
        if (change.IsCreate(out var update)) {
            if (!update.Id.IsNone && !update.Id.Equals(id))
                throw StandardError.Constraint("Change settings id should be empty or match command id.");

            userSettings = new RouletteUserSettings(id) {
                IsEnabled = update.IsEnabled,
                Version = VersionGenerator.NextVersion(),
            };
            var dbUserSettings = new DbRouletteUserSettings(userSettings);
            dbContext.RouletteUserSettings.Add(dbUserSettings);
            userSettings = dbUserSettings.ToModel();
        }
        else {
            var dbUserSettings = await dbContext.RouletteUserSettings
                .Get(id, cancellationToken)
                .ConfigureAwait(false);
            var oldUserSettings = dbUserSettings?.ToModel();

            if (change.IsUpdate(out update)) {
                oldUserSettings.Require();
                oldUserSettings.RequireVersion(expectedVersion);
                userSettings = oldUserSettings with {
                    IsEnabled = update.IsEnabled,
                    Version = VersionGenerator.NextVersion(oldUserSettings.Version),
                };
                dbUserSettings!.UpdateFrom(userSettings);
            }
            else {
                if (expectedVersion is not null)
                    dbUserSettings.RequireVersion(expectedVersion);
                if (dbUserSettings is not null)
                    dbContext.Remove(dbUserSettings);
            }

            userSettings = dbUserSettings?.ToModel();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return userSettings;
    }

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

    public virtual async Task OnCreateCompletedChatRoulette(
        RouletteProfilesBackend_CreateCompletedChatRoulette command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = command.OwnerUserId.Value + ":" + command.OwnerUserId + ":" + command.PeerProfileId;
        var dbPrefs = new DbRouletteCompletion {
            Id = id,
            Version = VersionGenerator.NextVersion(),
            OwnerUserId = command.OwnerUserId,
            OwnerProfileId = command.OwnerProfileId,
            PeerUserId = command.PeerUserId,
            PeerProfileId = command.PeerProfileId,
            IsInitiatedByOwner = command.IsInitiatedByOwner,
            CompletedAt = command.CompletedAt,
            CompleteReason = command.CompleteReason,
        };
        dbContext.RouletteCompletions.Add(dbPrefs);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    // [EventHandler]
    public virtual async Task OnChatRouletteCompletedEvent(
        ChatRouletteCompletedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var chatRoulette = eventCommand.ChatRoulette;

        await Commander.Call(
                new RouletteProfilesBackend_CreateCompletedChatRoulette(
                    chatRoulette.UserId1,
                    chatRoulette.ProfileId1,
                    chatRoulette.UserId2,
                    chatRoulette.ProfileId2,
                    chatRoulette.UserId1 == chatRoulette.CompletedBy,
                    chatRoulette.CompletedAt,
                    chatRoulette.CompleteReason),
                true,
                cancellationToken)
            .ConfigureAwait(false);

        await Commander.Call(
                new RouletteProfilesBackend_CreateCompletedChatRoulette(
                    chatRoulette.UserId2,
                    chatRoulette.ProfileId2,
                    chatRoulette.UserId1,
                    chatRoulette.ProfileId1,
                    chatRoulette.UserId2 == chatRoulette.CompletedBy,
                    chatRoulette.CompletedAt,
                    chatRoulette.CompleteReason),
                true,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
