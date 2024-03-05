using System.Net.Mail;
using ActualChat.Chat;
using ActualChat.Commands;
using ActualChat.Users.Db;
using ActualChat.Users.Events;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccountsBackend
{
    private IAuthBackend? _authBackend;
    private IAvatarsBackend? _avatarsBackend;
    private IServerKvasBackend? _serverKvasBackend;
    private ContactGreeter? _contactGreeter;
    private IDbEntityResolver<string, DbAccount>? _dbAccountResolver;
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private IAuthBackend AuthBackend => _authBackend ??= Services.GetRequiredService<IAuthBackend>();
    private IAvatarsBackend AvatarsBackend => _avatarsBackend ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => _serverKvasBackend ??= Services.GetRequiredService<IServerKvasBackend>();
    private ContactGreeter ContactGreeter => _contactGreeter ??= Services.GetRequiredService<ContactGreeter>();
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => _dbAccountResolver ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            return null;

        // We _must_ have a dependency on AuthBackend.GetUser here
        var user = await AuthBackend.GetUser(default, userId, cancellationToken).ConfigureAwait(false);
        AccountFull? account;
        if (user == null) {
            account = GetGuestAccount(userId);
            if (account == null)
                return null;
        }
        else {
            var dbAccount = await DbAccountResolver.Get(userId, cancellationToken).ConfigureAwait(false);
            account = dbAccount?.ToModel(user);
            if (account == null)
                return null;

            if (IsAdmin(user))
                account = account with { IsAdmin = true };
        }

        // Adding Avatar
        var kvas = ServerKvasBackend.GetUserClient(account);
        var userAvatarSettings = await kvas.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        var avatarId = userAvatarSettings.DefaultAvatarId;
        if (avatarId.IsEmpty) // Default avatar isn't selected - let's pick the first one
            avatarId = userAvatarSettings.AvatarIds.GetOrDefault(0);

        var avatar = avatarId.IsEmpty
            ? GetDefaultAvatar(account)
            : await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false) // No avatars at all
                ?? GetDefaultAvatar(account);
        account = account with { Avatar = avatar };
        return account;
    }

    // [ComputeMethod]
    public virtual async Task<UserId> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var sid = identity.Id.Value;
        var dbUserIdentity = await dbContext.UserIdentities
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            .ConfigureAwait(false);
        return new UserId(dbUserIdentity?.DbUserId);
    }

    // Not a [ComputeMethod]!
    public async Task<ApiArray<UserId>> ListChanged(
        long minVersion,
        long maxVersion,
        UserId lastId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccounts = await dbContext.Accounts
            .Where(x => x.Version >= minVersion && x.Version <= maxVersion)
            .Where(x => !Constants.User.SSystemUserIds.Contains(x.Id))
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var accounts = dbAccounts.ConvertAll(x => new {
            Id = new UserId(x.Id), x.Version,
        });
        if (lastId.IsNone || Constants.User.SystemUserIds.Contains(lastId))
            return accounts.Select(x => x.Id).ToApiArray();

        var lastIdIndex = accounts.FindIndex(x => x.Id == lastId);
        if (lastIdIndex < 0 || accounts[lastIdIndex].Version > minVersion)
            return accounts.Select(x => x.Id).ToApiArray();

        return accounts[(lastIdIndex + 1)..].Select(x => x.Id).ToApiArray();
    }

    public async Task<AccountFull?> GetLastChanged(CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var id = new UserId(dbAccount?.Id);
        if (id.IsNone)
            return null;

        return await Get(id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(
        AccountsBackend_Update command,
        CancellationToken cancellationToken)
    {
        var (account, expectedVersion) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(account.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sid = account.Id;
        var dbAccount = await dbContext.Accounts.ForUpdate()
            .FirstOrDefaultAsync(a => a.Id == sid, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.RequireVersion(expectedVersion);
        var existing = await Get(account.Id, cancellationToken).ConfigureAwait(false);
        account = account with {
            Version = VersionGenerator.NextVersion(dbAccount.Version),
        };
        var mustGreet = dbAccount.IsGreetingCompleted && !account.IsGreetingCompleted;
        dbAccount.UpdateFrom(account);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        new AccountChangedEvent(dbAccount.ToModel(account.User), existing, ChangeKind.Update).EnqueueOnCompletion();
        if (mustGreet)
            ContactGreeter.Activate();
    }

    // [CommandHandler]
    public virtual async Task OnDelete(
        AccountsBackend_Delete command,
        CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, default);
            return;
        }

        var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
        if (existingAccount is null)
            return;

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.UserPresences
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Avatars
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.UserIdentities
            .Where(a => a.DbUserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Users
            .Where(a => a.Id == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Accounts
            .Where(a => a.Id == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        new AccountChangedEvent(existingAccount, existingAccount, ChangeKind.Remove).EnqueueOnCompletion();

        // authors
        var removeAuthorsCommand = new AuthorsBackend_Remove(ChatId.None, AuthorId.None, userId);
        await Commander.Call(removeAuthorsCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<bool> OnMoveChatToPlace(
        AccountsBackend_MoveChatToPlace command,
        CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;
        var chatSid = chatId.Value;

        if (Computed.IsInvalidating()) {
            // Skip invalidation. Value for old chat should no longer be requested.
            return default;
        }

        Log.LogInformation("OnMoveChatToPlace: starting, moving chat '{ChatId}' to place '{PlaceId}'", chatSid, placeId);
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var hasChanges = false;
        hasChanges |= await UpdateUserChatSettings(dbContext, chatId, newChatId, cancellationToken).ConfigureAwait(false);
        hasChanges |= await UpdateChatPositions(dbContext, chatId, newChatId, cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("OnMoveChatToPlace: completed");
        return hasChanges;
    }

    // Event handlers

    [EventHandler]
    public virtual Task OnNewUserEvent(NewUserEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask; // It just notifies GreetingDispatcher

        ContactGreeter.Activate();
        return Task.CompletedTask;
    }

    // Private methods

    internal static bool IsAdmin(User user)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        var email = user.GetEmail();
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
            return false;

        if (AdminEmails.Contains(email))
            return true; // Predefined admin email
        if (HasGoogleIdentity(user) && OrdinalEquals(emailAddress.Host, AdminEmailDomain))
            return true; // actual.chat email
        return false;
    }

    private static bool HasGoogleIdentity(User user)
        => HasIdentity(user, GoogleDefaults.AuthenticationScheme);

    private static bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);

    private static AccountFull? GetGuestAccount(UserId userId)
    {
        if (!userId.IsGuest)
            return null;

        var name = RandomNameGenerator.Default.Generate(userId);
        var user = new User(userId, name);
        var account = new AccountFull(user);
        return account;
    }

    private static AvatarFull GetDefaultAvatar(AccountFull account)
        => new(account.Id) {
            Name = account.FullName,
            AvatarKey = DefaultUserPicture.GetAvatarKey(account.Id),
            Bio = "",
        };

    private async Task<bool> UpdateUserChatSettings(
        UsersDbContext dbContext,
        ChatId oldChatId,
        ChatId newChatId,
        CancellationToken cancellationToken)
    {
        var oldChatSettingsSuffix = UserChatSettings.GetKvasKey(oldChatId);
        var newChatSettingsSuffix = UserChatSettings.GetKvasKey(newChatId);

        // Update UserChatSettings
#pragma warning disable CA1307
        var updateCount = await dbContext.KvasEntries
            .Where(c => c.Key.EndsWith(oldChatSettingsSuffix))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Key, c => c.Key.Replace(oldChatSettingsSuffix, newChatSettingsSuffix)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated {Count} UserChatSettings kvas records", updateCount);
        return updateCount > 0;
    }

    private async Task<bool> UpdateChatPositions(
        UsersDbContext dbContext,
        ChatId oldChatId,
        ChatId newChatId,
        CancellationToken cancellationToken)
    {
        var oldChatPositionSuffix = $" {oldChatId.Value}:0";
        var newChatPositionSuffix = $" {newChatId.Value}:0";

        // Update UserChatSettings
#pragma warning disable CA1307
        var updateCount = await dbContext.ChatPositions
            .Where(c => c.Id.EndsWith(oldChatPositionSuffix))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Id, c => c.Id.Replace(oldChatPositionSuffix, newChatPositionSuffix)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated {Count} ChatPositions records", updateCount);
        return updateCount > 0;
    }
}
