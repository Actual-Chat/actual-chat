using System.Net.Mail;
using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccountsBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) {
        "alex.yakunin@gmail.com",
        "ustinovas@gmail.com",
    };

    [field: AllowNull, MaybeNull]
    private IAuthBackend AuthBackend => field ??= Services.GetRequiredService<IAuthBackend>();
    [field: AllowNull, MaybeNull]
    private IAvatarsBackend AvatarsBackend => field ??= Services.GetRequiredService<IAvatarsBackend>();
    [field: AllowNull, MaybeNull]
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    [field: AllowNull, MaybeNull]
    private ContactGreeter ContactGreeter => field ??= Services.GetRequiredService<ContactGreeter>();
    [field: AllowNull, MaybeNull]
    private FlowRegistry FlowRegistry => field ??= Services.GetRequiredService<FlowRegistry>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        // We _must_ have a dependency on AuthBackend.GetUser here
        var user = await AuthBackend.GetUser(DbShard.Single, userId.Value, cancellationToken).ConfigureAwait(false);
        AccountFull? account;
        if (user == null) {
            account = GetGuestAccount(userId);
            if (account == null)
                return null;
        }
        else {
            var dbAccount = await DbAccountResolver.Get(userId.Value, cancellationToken).ConfigureAwait(false);
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
            ? GetFallbackAvatar(account)
            : await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false) // No avatars at all
                ?? GetFallbackAvatar(account);
        account = account with { Avatar = avatar };
        return account;
    }

    // [ComputeMethod]
    public virtual async Task<UserId?> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var id = identity.Id;
        var dbUserIdentity = await dbContext.UserIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return UserId.ParseNullable(dbUserIdentity?.DbUserId);
    }

    // [ComputeMethod]
    public virtual async Task<UserId?> GetIdByAlias(AliasId aliasId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var aliasSid = aliasId.NormalizedValue;
        var accountId = await dbContext.Accounts
            .Where(x => x.AliasId == aliasSid)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return UserId.ParseNullable(accountId);
    }

    // Not a [ComputeMethod]!
    public async Task<UserId[]> ListChanged(
        long minVersion,
        long maxVersion,
        UserId? lastId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var accountsQuery = lastId is null
            ? dbContext.Accounts.Where(x => x.Version >= minVersion && x.Version <= maxVersion)
            : dbContext.Accounts.Where(x => (x.Version > minVersion && x.Version <= maxVersion)
                || (x.Version == minVersion && string.Compare(x.Id, lastId.Value) > 0));

        var dbAccounts = await accountsQuery
            .Where(x => !Constants.User.SystemUserIdValues.Contains(x.Id))
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAccounts.Select(x => UserId.Parse(x.Id)).ToArray();
    }

    public async Task<AccountFull?> GetLastChanged(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var id = UserId.ParseNullable(dbAccount?.Id);
        if (id is null)
            return null;

        return await Get(id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(
        AccountsBackend_Update command,
        CancellationToken cancellationToken)
    {
        var (account, expectedVersion) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = Get(account.Id, default);
            var invAliasIds = context.Operation.Items.KeylessGet<List<AliasId>>();
            if (invAliasIds is not null)
                foreach (var invAliasId in invAliasIds)
                    _ = GetIdByAlias(invAliasId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var accountIdValue = account.Id.Value;
        var dbAccount = await dbContext.Accounts.ForUpdate()
            .FirstOrDefaultAsync(a => a.Id == accountIdValue, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.RequireVersion(expectedVersion);
        var existing = await Get(account.Id, cancellationToken).ConfigureAwait(false);
        account = account with {
            Version = VersionGenerator.NextVersion(dbAccount.Version),
        };
        var mustGreet = dbAccount.IsGreetingCompleted && !account.IsGreetingCompleted;
        var mustStartDigestFlow = !OrdinalEquals(dbAccount.TimeZone, account.TimeZone);
        dbAccount.UpdateFrom(account);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.AddEvent(
            new AccountChangedEvent(dbAccount.ToModel(account.User), existing, ChangeKind.Update));
        if (mustGreet)
            ContactGreeter.Activate();

        if (mustStartDigestFlow) {
            Log.LogInformation("Digest flow reset for: {AccountId}", account.Id);
            var e = new FlowResetEvent(FlowRegistry.NewId<DigestFlow>(account.Id.Value), "Account change");
            context.Operation.AddEvent(e);
        }

        var oldAliasId = existing?.AliasId;
        var newAliasId = account.AliasId;
        if (oldAliasId != newAliasId) {
            var aliasesToInvalidate = new List<AliasId>();
            if (oldAliasId is not null)
                aliasesToInvalidate.Add(oldAliasId);
            if (newAliasId is not null)
                aliasesToInvalidate.Add(newAliasId);
            context.Operation.Items.KeylessSet(aliasesToInvalidate);
        }
    }

    // [CommandHandler]
    public virtual async Task OnDelete(
        AccountsBackend_Delete command,
        CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive) {
            _ = Get(userId, default);
            return;
        }

        var context = CommandContext.GetCurrent();
        var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
        if (existingAccount is null)
            return;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.UserPresences
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Avatars
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.UserIdentities
            .Where(a => a.DbUserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Users
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Accounts
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(
            new AccountChangedEvent(existingAccount, existingAccount, ChangeKind.Remove));

        // authors
        var removeAuthorsCommand = new AuthorsBackend_Remove(null, null, userId);
        await Commander.Call(removeAuthorsCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Event handlers

    // [EventHandler]
    public virtual Task OnNewUserEvent(NewUserEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just notifies GreetingDispatcher

        ContactGreeter.Activate();
        return Task.CompletedTask;
    }

    // Private methods

    internal static bool IsAdmin(User user)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") && OrdinalEquals(user.Id, Constants.User.Admin.UserId.Value))
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

        var name = RandomNameGenerator.Default.Generate(userId.Value);
        var user = new User(userId.Value, name);
        var account = new AccountFull(user);
        return account;
    }

    private static AvatarFull GetFallbackAvatar(AccountFull account)
        => new(account.Id) {
            Name = account.Name,
            AvatarKey = DefaultUserPicture.GetAvatarKey(account.Id.Value),
            Bio = "",
        };
}
