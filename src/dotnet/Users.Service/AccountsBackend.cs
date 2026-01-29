using System.Net.Mail;
using System.Security.Claims;
using ActualChat.Chat;
using ActualChat.Db;
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
    private AccountNameValidator AccountNameValidator => field ??= Services.GetRequiredService<AccountNameValidator>();

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var dbAccount = await DbAccountResolver.Get(userId.Value, cancellationToken).ConfigureAwait(false);

        AccountFull? account;
        if (dbAccount == null) {
            account = GetGuestAccount(userId);
            if (account == null)
                return null;
        }
        else if (dbAccount.FormatVersion >= 1) {
            // FormatVersion >= 1: all data is in DbAccount, no need to fetch DbUser
            account = dbAccount.ToModel();
            if (IsAdmin(account))
                account = account with { IsAdmin = true };
        }
        else {
            // FormatVersion == 0: need to fall back to DbUser for Name/Claims/Identities
            var dbUser = await DbUserResolver.Get(userId.Value, cancellationToken).ConfigureAwait(false);
            if (dbUser == null)
                return null;

            account = dbAccount.ToModel(dbUser);
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

        // Try DbAccountIdentity first (for FormatVersion >= 1 accounts)
        var dbAccountIdentity = await dbContext.AccountIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (dbAccountIdentity is not null)
            return UserId.ParseNullable(dbAccountIdentity.DbAccountId);

        // Fall back to DbUserIdentity (for FormatVersion == 0 accounts)
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
    public virtual async Task OnSignIn(AccountsBackend_SignIn command, CancellationToken cancellationToken = default)
    {
        var (session, authenticatedIdentity, identities, claims) = command;
        session.RequireValid();

        identities = identities.With(authenticatedIdentity, "");

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invAccount = context.Operation.Items.KeylessGet<AccountFull>();
            if (invAccount is not null) {
                _ = Get(invAccount.Id, default);
                foreach (var (invIdentity, _) in invAccount.Identities)
                    _ = GetIdByUserIdentity(invIdentity, default);
            }
            return;
        }

        // Check if session is valid (not forced sign-out)
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo?.IsSignOutForced == true)
            throw StandardError.Unauthorized("Session unavailable.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var isNew = false;
        AccountFull? resultAccount = null;
        var userId = await dbContext
            .GetUserIdByIdentity(authenticatedIdentity, true, cancellationToken)
            .ConfigureAwait(false);
        AccountFull? existingAccount;
        if (userId is null) {
            // No user found by identity - create new account
            existingAccount = UpdateExistingAccount(null, null);
            var dbAccount = await CreateDbAccount(dbContext, existingAccount, cancellationToken).ConfigureAwait(false);
            userId = UserId.Parse(dbAccount.Id);
            resultAccount = dbAccount.ToModel();
            isNew = true;
        }
        else {
            // Existing user found by identity - acquire lock first, then load and update
            existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
            await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);
            var dbAccount = await dbContext.GetDbAccount(userId, true, cancellationToken).ConfigureAwait(false);
            dbAccount.Require();
            var dbUser = await dbContext.GetDbUser(userId, true, cancellationToken).ConfigureAwait(false);
            dbUser.Require();

            existingAccount = UpdateExistingAccount(existingAccount, userId);
            await UpdateDbAccount(dbContext, dbAccount, dbUser, existingAccount, cancellationToken).ConfigureAwait(false);
        }

        context.Operation.Items.KeylessSet(resultAccount);
        context.Operation.Items.KeylessSet(isNew);

        // Update session with auth info
        var upsertCommand = new SessionsBackend_Upsert(session, "", "", default, userId.Value, authenticatedIdentity);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        // Emit NewUserEvent if this is a new user
        if (isNew) {
            context.Operation.AddEvent(new NewAccountEvent(userId));

            // Auto-enable early access settings for test agent accounts
            if (identities.GetEmails().Any(Constants.Auth.TestAgent.IsTestAgentEmail)) {
                var kvas = ServerKvasBackend.GetUserClient(userId);
                await kvas.UserAppSettings().Set(new UserAppSettings {
                    AreExperimentalFeaturesEnabled = true,
                    IsIncompleteUIEnabled = true,
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        AccountFull UpdateExistingAccount(AccountFull? account, UserId? newUserId)
        {
            account ??= new AccountFull("");
            var mergedIdentities = account.Identities.WithMany(identities);
            var mergedClaims = account.Claims.WithMany(claims);

            // Set Email from claims or identities if not already set
            var email = account.Email.IsNullOrEmpty()
                ? mergedClaims.GetValueOrDefault(ClaimTypes.Email, "").NullIfEmpty()
                    ?? mergedIdentities.GetEmails().FirstOrDefault()
                    ?? ""
                : account.Email;

            // Set Phone from identities if not already set
            var phone = account.Phone ?? mergedIdentities.GetPhones().FirstOrDefault();

            return account with {
                Id = newUserId ?? account.Id,
                Name = GetNewAccountName(account.Name),
                Email = email,
                Phone = phone,
                Claims = mergedClaims,
                Identities = mergedIdentities,
            };
        }

        string GetNewAccountName(string? existingName)
        {
            if (!existingName.IsNullOrEmpty())
                return AccountNameValidator.Normalize(existingName);

            existingName = claims.GetValueOrDefault(ClaimTypes.GivenName, "");
            var surname = claims.GetValueOrDefault(ClaimTypes.Surname, "");
            if (!surname.IsNullOrEmpty())
                existingName = $"{existingName} {surname}";
            return AccountNameValidator.Normalize(existingName);
        }
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(AccountsBackend_Update command, CancellationToken cancellationToken)
    {
        var (account, expectedVersion) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invAccount = context.Operation.Items.KeylessGet<AccountFull>();
            if (invAccount is not null) {
                _ = Get(invAccount.Id, default);
                foreach (var (invIdentity, _) in invAccount.Identities)
                    _ = GetIdByUserIdentity(invIdentity, default);
            }
            var invAliasIds = context.Operation.Items.KeylessGet<List<AliasId>>();
            if (invAliasIds is not null)
                foreach (var invAliasId in invAliasIds)
                    _ = GetIdByAlias(invAliasId, default);
            return;
        }

        var userId = account.Id;
        var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
        existingAccount.Require().RequireVersion(expectedVersion);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts.Include(a => a.Identities)
            .FirstOrDefaultAsync(a => a.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.Require().RequireVersion(expectedVersion);
        var dbUser = await dbContext.GetDbUser(userId, true, cancellationToken).ConfigureAwait(false);
        dbUser.Require();

        var mustGreet = !account.IsGreetingCompleted && dbAccount.IsGreetingCompleted;
        var mustResetDigestFlow = !OrdinalEquals(dbAccount.TimeZone, account.TimeZone);
        account = account with {
            Version = VersionGenerator.NextVersion(dbAccount.Version),
            Name = AccountNameValidator.Normalize(account.Name),
        };
        dbAccount.UpdateFrom(account);
        dbUser.UpdateFrom(account);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        account = dbAccount.ToModel();
        context.Operation.Items.KeylessSet(account);
        var oldAliasId = existingAccount.AliasId;
        var newAliasId = account.AliasId;
        if (oldAliasId != newAliasId) {
            var aliasesToInvalidate = new List<AliasId>();
            if (oldAliasId is not null)
                aliasesToInvalidate.Add(oldAliasId);
            if (newAliasId is not null)
                aliasesToInvalidate.Add(newAliasId);
            context.Operation.Items.KeylessSet(aliasesToInvalidate);
        }
        context.Operation.AddEvent(new AccountChangedEvent(account, existingAccount, ChangeKind.Update));

        if (mustGreet)
            ContactGreeter.Activate();
        if (mustResetDigestFlow) {
            Log.LogInformation("Scheduling DigestFlow reset for {AccountId}", account.Id);
            var flowId = FlowHub.NewId<DigestFlow>(account.Id.Value);
            context.Operation.AddEvent(FlowHub.NewResumeEvent(flowId).WithReset());
        }
    }

    // [CommandHandler]
    public virtual async Task OnDelete(AccountsBackend_Delete command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invAccount = context.Operation.Items.KeylessGet<AccountFull>();
            if (invAccount is not null) {
                _ = Get(invAccount.Id, default);
                foreach (var (invIdentity, _) in invAccount.Identities)
                    _ = GetIdByUserIdentity(invIdentity, default);
                if (invAccount.AliasId is { } invAliasId)
                    _ = GetIdByAlias(invAliasId, default);
            }
            return;
        }

        var account = await Get(userId, cancellationToken).Require().ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Acquire lock before deleting to prevent conflicts with concurrent updates
        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        await dbContext.UserPresences
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Avatars
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.AccountIdentities
            .Where(a => a.DbAccountId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.UserIdentities
            .Where(a => a.DbUserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Accounts
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Users
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.AddEvent(new AccountChangedEvent(account, account, ChangeKind.Remove));

        // Authors
        var removeAuthorsCommand = new AuthorsBackend_Remove(null, null, userId);
        await Commander.Call(removeAuthorsCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Event handlers

    // [EventHandler]
    public virtual Task OnNewAccountEvent(NewAccountEvent eventCommand, CancellationToken cancellationToken)
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

        var emails = account.Identities.GetEmails();
        foreach (var email in emails) {
            if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
                continue;

            if (AdminEmails.Contains(email))
                return true; // Predefined admin email
            if (HasGoogleIdentity(account) && OrdinalEquals(emailAddress.Host, AdminEmailDomain))
                return true; // company email
            if (Constants.Auth.TestAgent.IsTestAgentEmail(email))
                return true; // test agent email
        }
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

    // DbUser/DbAccount sync helpers

    private async Task UpdateDbAccount(
        UsersDbContext dbContext,
        DbAccount dbAccount,
        DbUser dbUser,
        AccountFull account,
        CancellationToken cancellationToken)
    {
        // Update DbAccount with all properties (double write)
        dbAccount.Version = VersionGenerator.NextVersion(dbAccount.Version);
        dbAccount.FormatVersion = 2; // Upgrade to format version 2
        dbAccount.Status = account.Status;
        dbAccount.Email = account.Email;
        dbAccount.IsEmailVerified = account.IsEmailVerified();
        dbAccount.Phone = account.Phone?.Value ?? "";
        dbAccount.SyncContacts = account.SyncContacts;
        dbAccount.Name = account.Name;
        dbAccount.IsGreetingCompleted = account.IsGreetingCompleted;
        dbAccount.TimeZone = account.TimeZone;
        dbAccount.AliasId = account.AliasId?.NormalizedValue ?? "";
        dbAccount.Claims = dbUser.Claims; // Sync claims from DbUser

        // Update DbUser
        dbUser.UpdateFrom(account);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // DbUser/DbAccount creation

    private async Task<DbAccount> CreateDbAccount(
        UsersDbContext dbContext, AccountFull account, CancellationToken cancellationToken)
    {
        // Generate user ID in code - no need for a DB roundtrip
        var userId = account.Id?.Value.NullIfEmpty() ?? UserId.New().Value;

        // Construct display name from claims - used for both DbUser and DbAccount
        var name = account.Claims.GetValueOrDefault(ClaimTypes.GivenName, "");
        var lastName = account.Claims.GetValueOrDefault(ClaimTypes.Surname, "");
        if (!lastName.IsNullOrEmpty())
            name = $"{name} {lastName}";
        // Fall back to account.Name if no claims-based name is available
        if (name.IsNullOrEmpty())
            name = account.Name;
        // Normalize name
        name = AccountNameValidator.Normalize(name);

        account = account with {
            Id = UserId.Parse(userId),
            Name = name,
        };

        // Acquire lock before creating User and Account (use Accounts for consistency)
        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        // Create DbUser
        var dbUser = new DbUser() {
            Id = userId,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Claims = account.Claims.ToImmutableDictionary(StringComparer.Ordinal),
        };

        // Handle email identities
        var emailString = account.Claims.GetValueOrDefault(ClaimTypes.Email, "").NullIfEmpty()
            ?? account.Email.NullIfEmpty()
            ?? "";
        if (!emailString.IsNullOrEmpty() && ActualChat.Email.TryParse(emailString, out var email))
            account = account.WithEmailIdentity(email);

        // Update dbUser with identities
        dbUser.UpdateFrom(account);
        dbContext.Add(dbUser);

        // Create DbAccount with same data as DbUser (FormatVersion=1 means all data is in DbAccount)
        var context = CommandContext.GetCurrent();
        var isAdmin = IsAdmin(account);
        var dbAccount = new DbAccount {
            Id = userId,
            FormatVersion = 2, // New accounts start with format version 2
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = name, // Same normalized name as DbUser
            Email = emailString,
            IsEmailVerified = account.IsEmailVerified(),
            Phone = account.Phone?.Value ?? account.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = dbUser.CreatedAt,
            Claims = dbUser.Claims, // Sync claims to DbAccount
        };
        // Sync identities to DbAccount
        foreach (var (userIdentity, secret) in account.Identities) {
            if (!userIdentity.IsValid)
                continue;
            dbAccount.Identities.Add(new DbAccountIdentity {
                Id = userIdentity.Id,
                DbAccountId = userId,
                Secret = secret ?? "",
            });
        }
        dbContext.Accounts.Add(dbAccount);

        // Single commit for both User and Account
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accountModel = dbAccount.ToModel(account.Identities, account.Claims);
        context.Operation.AddEvent(new AccountChangedEvent(accountModel, null, ChangeKind.Create));
        return dbAccount;
    }
}
