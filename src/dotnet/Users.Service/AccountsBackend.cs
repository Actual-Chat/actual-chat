using System.Net.Mail;
using System.Security.Claims;
using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class AccountsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccountsBackend
{
    private const string AdminEmailDomain = Constants.Team.EmailDomain;
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) {
        "alex.yakunin@gmail.com",
        "ustinovas@gmail.com",
    };

    private ISessionsBackend SessionsBackend => field ??= Services.GetRequiredService<ISessionsBackend>();
    private IAvatarsBackend AvatarsBackend => field ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ContactGreeter ContactGreeter => field ??= Services.GetRequiredService<ContactGreeter>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    private IDbEntityResolver<string, DbUser> DbUserResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbUser>>();
    private UsersSettings UsersSettings => field ??= Services.GetRequiredService<UsersSettings>();
    private UserNamer UserNamer => field ??= Services.GetRequiredService<UserNamer>();

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        // We _must_ have a dependency on GetUser here
        var user = await GetUser(userId.Value, cancellationToken).ConfigureAwait(false);
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

            if (IsAdmin(account))
                account = account with { IsAdmin = true };
        }

        // Adding Avatar
        var kvas = ServerKvasBackend.GetUserClient(account);
        var userAvatarSettings = await kvas.UserAvatarSettings().Get(cancellationToken).ConfigureAwait(false);
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

    // [ComputeMethod]
    public virtual async Task<User?> GetUser(
        string userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsNullOrEmpty())
            return null;

        var dbUser = await DbUserResolver.Get(userId, cancellationToken).ConfigureAwait(false);
        return dbUser?.ToModel();
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
    public virtual async Task OnSignIn(AccountsBackend_SignIn command, CancellationToken cancellationToken = default)
    {
        var (session, account, authenticatedIdentity) = command;
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invUserId = context.Operation.Items.KeylessGet<UserId>();
            if (invUserId is not null) {
                _ = GetUser(invUserId.Value, default);
                _ = Get(invUserId, default);
            }
            // Invalidate GetUser if name was normalized
            if (context.Operation.Items.KeylessGet<UserNameChangedMeta>()?.Changed == true && invUserId is not null)
                _ = GetUser(invUserId.Value, default);
            return;
        }

        if (!account.Identities.ContainsKey(authenticatedIdentity))
#pragma warning disable MA0015
            throw new ArgumentOutOfRangeException(
                $"{nameof(command)}.{nameof(AccountsBackend_SignIn.AuthenticatedIdentity)}");
#pragma warning restore MA0015

        // Check if session is valid (not forced sign-out)
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.IsSignOutForced == true)
            throw StandardError.Unauthorized("Session unavailable.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var isNewUser = false;
        var dbUser = await dbContext.GetDbUserByUserIdentity(authenticatedIdentity, true, cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is null) {
            (dbUser, isNewUser) = await GetOrCreateDbUserOnSignIn(dbContext, account, cancellationToken)
                .ConfigureAwait(false);
            if (isNewUser == false) {
                dbUser.UpdateFrom(account, VersionGenerator);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else {
            account = account with {
                Id = UserId.Parse(dbUser.Id ?? "")
            };
            dbUser.UpdateFrom(account, VersionGenerator);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var userId = UserId.Parse(dbUser.Id ?? "");
        context.Operation.Items.KeylessSet(userId);
        context.Operation.Items.KeylessSet(isNewUser);

        // Normalize user name if needed
        var newName = UserNamer.NormalizeName(dbUser.Name);
        if (!OrdinalEquals(newName, dbUser.Name)) {
            context.Operation.Items.KeylessSet(new UserNameChangedMeta(true));
            dbUser.Name = newName;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Update session with auth info
        var upsertCommand = new SessionsBackend_Upsert(
            session, "", "", default,
            dbUser.Id ?? "",
            authenticatedIdentity);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        // Emit NewUserEvent if this is a new user
        if (isNewUser)
            context.Operation.AddEvent(new NewUserEvent(userId));
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
            // Invalidate GetUser if user name was changed
            if (context.Operation.Items.KeylessGet<UserNameChangedMeta>()?.Changed == true)
                _ = GetUser(account.Id.Value, default);
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
        var mustResetDigestFlow = !OrdinalEquals(dbAccount.TimeZone, account.TimeZone);
        dbAccount.UpdateFrom(account);

        // Update User name if it changed (User and Account should stay in sync)
        var existingUserName = existing?.Name ?? "";
        var newUserName = account.Name;
        if (!OrdinalEquals(existingUserName, newUserName)) {
            var dbUser = await dbContext.GetDbUser(accountIdValue, true, cancellationToken).ConfigureAwait(false);
            if (dbUser != null) {
                dbUser.Name = newUserName;
                dbUser.Version = VersionGenerator.NextVersion(dbUser.Version);
                context.Operation.Items.KeylessSet(new UserNameChangedMeta(true));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accountModel = dbAccount.ToModel(account.Identities, account.Claims);
        context.Operation.AddEvent(
            new AccountChangedEvent(accountModel, existing, ChangeKind.Update));
        if (mustGreet)
            ContactGreeter.Activate();

        if (mustResetDigestFlow) {
            Log.LogInformation("Scheduling DigestFlow reset for {AccountId}", account.Id);
            var flowId = FlowHub.NewId<DigestFlow>(account.Id.Value);
            context.Operation.AddEvent(FlowHub.NewResumeEvent(flowId).WithReset());
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

    internal static bool IsAdmin(AccountFull account)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(account, "internal") && account.Id == Constants.User.Admin.UserId)
            return true;

        var email = account.Identities.GetEmail();
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
            return false;

        if (AdminEmails.Contains(email))
            return true; // Predefined admin email
        if (HasGoogleIdentity(account) && OrdinalEquals(emailAddress.Host, AdminEmailDomain))
            return true; // company email
        return false;
    }

    private static bool HasGoogleIdentity(AccountFull account)
        => HasIdentity(account, GoogleDefaults.AuthenticationScheme);

    private static bool HasIdentity(AccountFull account, string provider)
        => account.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);

    private static AccountFull? GetGuestAccount(UserId userId)
    {
        if (!userId.IsGuest)
            return null;

        var name = RandomNameGenerator.Default.Generate(userId.Value);
        return new AccountFull(userId, 0) { Name = name };
    }

    private static AvatarFull GetFallbackAvatar(AccountFull account)
        => new(account.Id) {
            Name = account.Name,
            AvatarKey = DefaultUserPicture.GetAvatarKey(account.Id.Value),
            Bio = "",
        };

    // DbUser methods (inlined from DbUserRepo)

    private async Task<DbUser> CreateDbUser(
        UsersDbContext dbContext, AccountFull account, CancellationToken cancellationToken)
    {
        // Construct display name from claims - used for both DbUser and DbAccount
        var name = account.Claims.GetValueOrDefault(ClaimTypes.GivenName, "");
        var lastName = account.Claims.GetValueOrDefault(ClaimTypes.Surname, "");
        if (!lastName.IsNullOrEmpty())
            name = $"{name} {lastName}";
        // Fall back to account.Name if no claims-based name is available
        if (name.IsNullOrEmpty())
            name = account.Name;

        // Creating "base" dbUser
        var dbUser = new DbUser() {
            Id = account.Id?.Value.NullIfEmpty() ?? UserId.New().Value,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Claims = account.Claims.ToImmutableDictionary(StringComparer.Ordinal),
        };
        dbContext.Add(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        account = account with {
            Id = UserId.Parse(dbUser.Id ?? ""),
            Name = name, // Ensure account.Name matches DbUser.Name
        };
        // Updating dbUser from the model to persist account.Identities
        dbUser.UpdateFrom(account, VersionGenerator);
        dbContext.Update(dbUser);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ActualChat-specific: Create DbAccount for new user
        var context = CommandContext.GetCurrent();
        var isAdmin = IsAdmin(account);
        var dbAccount = new DbAccount {
            Id = account.Id.Value,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Email = account.Claims.GetValueOrDefault(ClaimTypes.Email, ""),
            Phone = account.Phone?.Value ?? account.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = dbUser.CreatedAt,
        };
        dbContext.Accounts.Add(dbAccount);

        var emailString = dbAccount.Email;
        if (!emailString.IsNullOrEmpty() && ActualChat.Email.TryParse(emailString, out var email)) {
            account = account.WithEmailIdentities(email);
            dbUser.UpdateFrom(account, VersionGenerator);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var accountModel = dbAccount.ToModel(account.Identities, account.Claims);
        context.Operation.AddEvent(new AccountChangedEvent(accountModel, null, ChangeKind.Create));
        return dbUser;
    }

    private async Task<(DbUser DbUser, bool IsCreated)> GetOrCreateDbUserOnSignIn(
        UsersDbContext dbContext, AccountFull account, CancellationToken cancellationToken)
    {
        DbUser? dbUser;
        if (account.Id is not null && !account.Id.Value.IsNullOrEmpty()) {
            dbUser = await dbContext.GetDbUser(account.Id.Value, false, cancellationToken).ConfigureAwait(false);
            if (dbUser is not null)
                return (dbUser, false);
        }

        // No user found, let's create it
        dbUser = await CreateDbUser(dbContext, account, cancellationToken).ConfigureAwait(false);
        return (dbUser, true);
    }

    // Nested types

    // Must be Newtonsoft.Json serializable - stored in Operation.Items
    private sealed record UserNameChangedMeta(bool Changed);
}
