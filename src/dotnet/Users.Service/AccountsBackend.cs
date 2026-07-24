using System.Net.Mail;
using System.Security.Claims;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Security;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

/// <summary>
/// Backend service implementation for user account management.
/// </summary>
public class AccountsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccountsBackend
{
    private const string AdminEmailDomain = Constants.Team.EmailDomain;
    private static HashSet<string> AdminEmails { get; } = [
        "alex.yakunin@gmail.com",
        "ustinovas@gmail.com",
        "crui3er@gmail.com",
    ];

    private ISessionsBackend SessionsBackend => field ??= Services.GetRequiredService<ISessionsBackend>();
    private IAvatarsBackend AvatarsBackend => field ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ContactGreeter ContactGreeter => field ??= Services.GetRequiredService<ContactGreeter>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    private UsersSettings UsersSettings => field ??= Services.GetRequiredService<UsersSettings>();
    private AccountNameValidator AccountNameValidator => field ??= Services.GetRequiredService<AccountNameValidator>();
    private ISecureTokensBackend SecureTokensBackend => field ??= Services.GetRequiredService<ISecureTokensBackend>();

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
        else {
            account = dbAccount.ToModel();
            if (IsAdmin(account))
                account = account with { IsAdmin = true };
        }

        // Adding Avatar
        var kvas = ServerKvasBackend.ForUser(account);
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

        var dbAccountIdentity = await dbContext.AccountIdentities
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return dbAccountIdentity is not null
            ? UserId.ParseNullable(dbAccountIdentity.DbAccountId)
            : null;
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
    public virtual async Task<ApiList<Session>> ListSessions(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var sessionIds = await dbContext.UserSessions
            .Where(x => x.UserId == userId.Value)
            .Select(x => x.SessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return sessionIds.Select(x => new Session(x)).ToApiList();
    }

    // Not a [ComputeMethod]!
    public async Task<Account[]> ListChanged(
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

        return dbAccounts.Select(x => x.ToAccount()).ToArray();
    }

    // Not a [ComputeMethod]!
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
        var (session, authenticatedIdentity, identities, claims, autoCreate) = command;
        session.RequireValid();
        if (session.Kind is not SessionKind.Session)
            throw StandardError.Constraint("Regular Session is required here.");

        identities = identities.With(authenticatedIdentity, "");
        _ = identities.HasInternalIdentity(out var internalUserId);

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

        // Check if session is valid (not expired)
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is { IsActive: false })
            throw StandardError.Unavailable($"This {session.Kind.ToReadable()} is expired.");
        if (sessionInfo?.UserId is not null)
            throw StandardError.Constraint("Already signed in.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var isNew = false;
        AccountFull? account;
        var userId = await dbContext
            .GetUserIdByIdentity(authenticatedIdentity, true, cancellationToken)
            .ConfigureAwait(false);

        // If not found by authenticatedIdentity, try fallback lookup by other identities
        // (e.g., provider identity not in DB yet, but email identity links to existing user)
        if (userId is null) {
            foreach (var (fallbackIdentity, _) in identities) {
                if (!fallbackIdentity.IsValid || fallbackIdentity == authenticatedIdentity)
                    continue;
                userId = await dbContext
                    .GetUserIdByIdentity(fallbackIdentity, true, cancellationToken)
                    .ConfigureAwait(false);
                if (userId is not null)
                    break;
            }
        }

        // If no user found by identity but internalUserId is provided, check if account exists by ID
        if (userId is null && internalUserId is not null) {
            var internalAccount = await Get(internalUserId, cancellationToken).ConfigureAwait(false);
            if (internalAccount is not null)
                userId = internalUserId;
        }

        if (userId is null) {
            if (!autoCreate) {
                // No account exists yet — stash a SecureToken with the sign-in payload and
                // surface it via SessionTemporals so the UI can ask the user to confirm
                // registration. Accounts.OnConfirmRegister re-issues this command with
                // AutoCreate=true to actually create the account.
                var pending = new PendingRegistration(session, authenticatedIdentity, identities, claims);
                var token = await pending.Encode(SecureTokensBackend, cancellationToken).ConfigureAwait(false);
                var info = new PendingRegistrationInfo(
                    Provider: AuthSchema.DisplayNames.GetValueOrDefault(authenticatedIdentity.Schema, authenticatedIdentity.Schema),
                    Identifier: GetPendingRegistrationIdentifier(authenticatedIdentity, identities, claims),
                    Token: token);
                var setCmd = new SessionTemporalsBackend_Set(
                    session, Constants.SessionTemporals.PendingRegistrationKey, info.ToJson());
                await Commander.Call(setCmd, true, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Confirmed registration - create new account
            account = UpdateExistingAccount(null, internalUserId);
            var dbAccount = await CreateDbAccount(dbContext, account, cancellationToken).ConfigureAwait(false);
            userId = UserId.Parse(dbAccount.Id);
            account = dbAccount.ToModel();
            isNew = true;
        }
        else {
            // Existing user found by identity or desired ID - acquire lock first, then load and update
            var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
            await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);
            var dbAccount = await dbContext.GetDbAccount(userId, true, cancellationToken).ConfigureAwait(false);
            dbAccount.Require();

            account = UpdateExistingAccount(existingAccount, userId);
            await UpdateDbAccount(dbContext, dbAccount, account, cancellationToken).ConfigureAwait(false);
        }

        context.Operation.Items.KeylessSet(account);
        context.Operation.Items.KeylessSet(isNew);

        var upsertCommand = new SessionsBackend_Upsert(session) {
            UserId = userId,
            AuthenticatedIdentity = authenticatedIdentity,
        };
        await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);

        // Clear any stale pending-registration prompt now that we have a real account.
        var clearPendingCmd = new SessionTemporalsBackend_Set(
            session, Constants.SessionTemporals.PendingRegistrationKey, null);
        await Commander.Call(clearPendingCmd, true, cancellationToken).ConfigureAwait(false);

        // Emit UserSignedInEvent
        context.Operation.AddEvent(new UserSignedInEvent(userId, session));
        context.Operation.AddEvent(FlowHub.NewResumeEvent<UserSignInFlow>(userId.Value));

        // Emit NewUserEvent if this is a new user
        if (isNew) {
            context.Operation.AddEvent(new NewAccountEvent(userId));

            // Auto-enable early access settings for test agent accounts
            if (identities.GetEmails().Any(Constants.Auth.TestAgent.IsTestAgentEmail)) {
                var kvas = ServerKvasBackend.ForUser(userId);
                await kvas.UserAppSettings().Set(new UserAppSettings {
                    AreExperimentalFeaturesEnabled = true,
                    IsIncompleteUIEnabled = true,
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        AccountFull UpdateExistingAccount(AccountFull? originalAccount, UserId? newUserId)
        {
            originalAccount ??= new AccountFull("");
            var mergedClaims = originalAccount.Claims.WithMany(claims);
            var mergedIdentities = originalAccount.Identities.WithMany(identities);

            // Add email identity for Google / Apple accounts based on email claim
            _ = ActualChat.Email.TryParse(claims.GetValueOrDefault(ClaimTypes.Email, ""), out var claimEmail);
            if (AuthSchema.IsExternal(authenticatedIdentity.Schema) && claimEmail is not null)
                mergedIdentities = mergedIdentities.WithEmailIdentity(claimEmail);

            var email = originalAccount.Email.NullIfEmpty() ?? mergedIdentities.GetEmails().FirstOrDefault() ?? "";
            var phone = originalAccount.Phone ?? mergedIdentities.GetPhones().FirstOrDefault();
            return originalAccount with {
                Id = newUserId ?? originalAccount.Id,
                Name = GetNewAccountName(originalAccount.Name),
                Email = email,
                Phone = phone,
                Claims = mergedClaims,
                Identities = mergedIdentities,
            };
        }

        string GetNewAccountName(string? originalName)
        {
            if (!originalName.IsNullOrEmpty())
                return AccountNameValidator.Normalize(originalName);

            originalName = claims.GetValueOrDefault(ClaimTypes.GivenName, "");
            var surname = claims.GetValueOrDefault(ClaimTypes.Surname, "");
            if (!surname.IsNullOrEmpty())
                originalName = $"{originalName} {surname}";
            return AccountNameValidator.Normalize(originalName);
        }
    }

    // [CommandHandler]
    public virtual async Task OnSignOut(AccountsBackend_SignOut command, CancellationToken cancellationToken = default)
    {
        var session = command.Session.RequireValid();
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive)
            return; // SessionsBackend_Upsert handles all invalidation

        // Check current session state
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is { IsActive: false })
            return; // Already expired

        var upsertCommand = new SessionsBackend_Upsert(session) {
            UserId = Option.Some<UserId?>(null),
            AuthenticatedIdentity = UserIdentity.None,
        };
        if (command.Deactivate)
            upsertCommand = upsertCommand with {
                ExpiresAt = Clocks.SystemClock.Now - TimeSpan.FromSeconds(10), // In the past
            };
        await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);

        // Emit event if user was signed in
        if (sessionInfo?.UserId is { } userId)
            context.Operation.AddEvent(new UserSignedOutEvent(userId, session));
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

        var mustGreet = !account.IsGreetingCompleted && dbAccount.IsGreetingCompleted;
        var mustResetDigestFlow = dbAccount.TimeZone != account.TimeZone;
        account = account with {
            Version = VersionGenerator.NextVersion(dbAccount.Version),
            Name = AccountNameValidator.Normalize(account.Name),
        };
        dbAccount.UpdateFrom(account);

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
                _ = ListSessions(invAccount.Id, default);
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

        await dbContext.UserSessions
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Accounts
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
        if (account.Identities.HasInternalIdentity() && account.Id == Constants.User.Admin.UserId)
            return true;

        var emails = account.Identities.GetEmails();
        foreach (var email in emails) {
            if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
                continue;

            // test-*@actual.chat accounts are never admins, even with a verified email
            if (Constants.Auth.TestAgent.IsTestAgentEmail(email))
                continue;

            if (AdminEmails.Contains(email))
                return true; // Predefined admin email
            if (emailAddress.Host == AdminEmailDomain)
                return true; // company email
        }
        return false;
    }

    private static string GetPendingRegistrationIdentifier(
        UserIdentity authenticatedIdentity,
        ApiMap<UserIdentity, string> identities,
        ApiMap<string, string> claims)
    {
        var schema = authenticatedIdentity.Schema;
        if (schema == AuthSchema.Phone || schema == AuthSchema.HashedPhone) {
            var phone = identities.GetPhones().FirstOrDefault();
            return phone is not null ? phone.Value : authenticatedIdentity.Value;
        }
        if (schema == AuthSchema.Email || schema == AuthSchema.HashedEmail)
            return identities.GetEmails().FirstOrDefault() ?? authenticatedIdentity.Value;
        // External providers (Google/Apple): show the email claim if available
        return claims.GetValueOrDefault(ClaimTypes.Email, "").NullIfEmpty()
            ?? identities.GetEmails().FirstOrDefault()
            ?? authenticatedIdentity.Value;
    }

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

    private async Task UpdateDbAccount(
        UsersDbContext dbContext,
        DbAccount dbAccount,
        AccountFull account,
        CancellationToken cancellationToken)
    {
        dbAccount.Version = VersionGenerator.NextVersion(dbAccount.Version);
        dbAccount.FormatVersion = 2;
        dbAccount.Status = account.Status;
        dbAccount.Email = account.Email;
        dbAccount.IsEmailVerified = account.IsEmailVerified();
        dbAccount.Phone = account.Phone?.Value ?? "";
        dbAccount.SyncContacts = account.SyncContacts;
        dbAccount.Name = account.Name;
        dbAccount.IsGreetingCompleted = account.IsGreetingCompleted;
        dbAccount.TimeZone = account.TimeZone;
        dbAccount.AliasId = account.AliasId?.NormalizedValue ?? "";
        dbAccount.Claims = account.Claims.ToImmutableDictionary();

        // Sync identities to DbAccount
        var dbIdentities = dbAccount.Identities.ToDictionary(ai => ai.Id);
        foreach (var (userIdentity, secret) in account.Identities) {
            if (!userIdentity.IsValid)
                continue;
            var foundIdentity = dbIdentities.GetValueOrDefault(userIdentity.Id);
            if (foundIdentity is not null) {
                foundIdentity.Secret = secret;
                continue;
            }

            // Never steal identities from other accounts
            var existingOwner = await dbContext
                .GetUserIdByIdentity(userIdentity, false, cancellationToken)
                .ConfigureAwait(false);
            if (existingOwner is not null)
                continue; // Already exists (for this or another account)

            dbAccount.Identities.Add(new DbAccountIdentity {
                Id = userIdentity.Id,
                DbAccountId = dbAccount.Id,
                Secret = secret ?? "",
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<DbAccount> CreateDbAccount(
        UsersDbContext dbContext, AccountFull account, CancellationToken cancellationToken)
    {
        // Generate user ID in code - no need for a DB roundtrip
        var userId = account.Id?.Value.NullIfEmpty() ?? UserId.New().Value;

        // Construct display name from claims
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

        // Acquire lock before creating Account
        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        // Handle email identities
        var emailString = account.Claims.GetValueOrDefault(ClaimTypes.Email, "").NullIfEmpty()
            ?? account.Email.NullIfEmpty()
            ?? "";
        if (!emailString.IsNullOrEmpty() && ActualChat.Email.TryParse(emailString, out var email))
            account = account.WithEmailIdentity(email);

        // Create DbAccount
        var context = CommandContext.GetCurrent();
        var isAdmin = IsAdmin(account);
        var dbAccount = new DbAccount {
            Id = userId,
            FormatVersion = 2,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
            Version = VersionGenerator.NextVersion(),
            Name = name,
            Email = emailString,
            IsEmailVerified = account.IsEmailVerified(),
            Phone = account.Phone?.Value ?? account.Claims.GetValueOrDefault(ClaimTypes.MobilePhone, ""),
            CreatedAt = Clocks.SystemClock.Now,
            Claims = account.Claims.ToImmutableDictionary(),
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

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accountModel = dbAccount.ToModel(account.Identities, account.Claims);
        context.Operation.AddEvent(new AccountChangedEvent(accountModel, null, ChangeKind.Create));
        return dbAccount;
    }
}
