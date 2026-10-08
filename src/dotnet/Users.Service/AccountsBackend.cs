using System.Net.Mail;
using System.Security.Claims;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Security;
using ActualChat.Users.Db;
using ActualChat.Users.Email;
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
    private const int ReserveUserIdAttemptCount = 5;
    private static HashSet<string> AdminEmails { get; } = [
        "alex.yakunin@gmail.com",
        "ustinovas@gmail.com",
        "crui3er@gmail.com",
    ];

    private ISessionsBackend SessionsBackend => field ??= Services.GetRequiredService<ISessionsBackend>();
    private ISessionTemporalsBackend SessionTemporalsBackend => field ??= Services.GetRequiredService<ISessionTemporalsBackend>();
    private IAvatarsBackend AvatarsBackend => field ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ContactGreeter ContactGreeter => field ??= Services.GetRequiredService<ContactGreeter>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    private UsersSettings UsersSettings => field ??= Services.GetRequiredService<UsersSettings>();
    private AccountNameValidator AccountNameValidator => field ??= Services.GetRequiredService<AccountNameValidator>();
    private ISecureTokensBackend SecureTokensBackend => field ??= Services.GetRequiredService<ISecureTokensBackend>();
    private HostInfo HostInfo => field ??= Services.HostInfo();

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
            if (dbAccount.Status == AccountStatus.Removed)
                return null;

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

    // [ComputeMethod] - consolidated
    public virtual async Task<bool> Exists(UserId userId, CancellationToken cancellationToken)
        // Consolidated: this sits on the chat tile path through AuthorsBackend.Exists,
        // and Get also depends on the avatar and the KVAS setting.
        // Without consolidation every avatar change would invalidate every tile the account appears in.
        => await Get(userId, cancellationToken).ConfigureAwait(false) is not null;

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
            await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);
            var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
            if (existingAccount is { IsBot: true })
                throw StandardError.Unauthorized("Bot accounts cannot sign in.");

            var dbAccount = await dbContext.GetDbAccount(userId, true, cancellationToken).ConfigureAwait(false);
            dbAccount.Require();

            account = UpdateExistingAccount(existingAccount, userId);
            var originalIdentities = existingAccount?.Identities ?? new ApiMap<UserIdentity, string>();
            await UpdateDbAccount(dbContext, dbAccount, account, originalIdentities, cancellationToken)
                .ConfigureAwait(false);
        }

        Invalidation.Defer(() => {
            _ = Get(account.Id, default);
            foreach (var (identity, _) in account.Identities)
                _ = GetIdByUserIdentity(identity, default);
        });

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
        await RecordSignInUsage(context, session, userId, isNew && !account.IsBot, sessionInfo, cancellationToken)
            .ConfigureAwait(false);
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

            // Add email identity for Google / Apple accounts based on a verified email claim
            _ = ActualChat.Email.TryParse(claims.GetValueOrDefault(ClaimTypes.Email, ""), out var claimEmail);
            if (AuthSchema.IsExternal(authenticatedIdentity.Schema)
                && AuthSchema.HasVerifiedEmail(claims)
                && claimEmail is not null)
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
        var userId = account.Id;
        var context = CommandContext.GetCurrent();

        var existingAccount = await Get(userId, cancellationToken).ConfigureAwait(false);
        existingAccount.Require().RequireVersion(expectedVersion);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts.Include(a => a.Identities)
            .FirstOrDefaultAsync(a => a.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.Require().RequireVersion(expectedVersion);
        if (dbAccount.Status == AccountStatus.Removed)
            throw StandardError.NotFound<Account>();

        foreach (var identity in account.Identities.Keys) {
            if (identity.Schema is not (AuthSchema.Email or AuthSchema.Phone))
                continue;

            var ownerId = await dbContext.GetUserIdByIdentity(identity, false, cancellationToken).ConfigureAwait(false);
            if (ownerId is not null && ownerId != userId) {
                var target = identity.Schema == AuthSchema.Email ? "Email" : "Phone number";
                throw StandardError.Unauthorized($"{target} has already been taken by another account.");
            }
        }

        var mustGreet = !account.IsGreetingCompleted && dbAccount.IsGreetingCompleted;
        var mustResumeDigestFlow = dbAccount.TimeZone != account.TimeZone
            || (!dbAccount.IsEmailVerified && account.IsEmailVerified());
        account = account with {
            Version = VersionGenerator.NextVersion(dbAccount.Version),
            Name = AccountNameValidator.Normalize(account.Name),
        };
        dbAccount.UpdateFrom(account, existingAccount.Identities);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        account = dbAccount.ToModel();
        var oldAliasId = existingAccount.AliasId;
        var newAliasId = account.AliasId;
        Invalidation.Defer(() => {
            _ = Get(account.Id, default);
            foreach (var (identity, _) in account.Identities)
                _ = GetIdByUserIdentity(identity, default);
            if (oldAliasId == newAliasId)
                return;

            if (oldAliasId is not null)
                _ = GetIdByAlias(oldAliasId, default);
            if (newAliasId is not null)
                _ = GetIdByAlias(newAliasId, default);
        });
        context.Operation.AddEvent(new AccountChangedEvent(account, existingAccount, ChangeKind.Update));

        if (mustGreet)
            ContactGreeter.Activate();
        if (mustResumeDigestFlow) {
            // The flow is parked on "no time zone" or "no verified email" for up to 2 days otherwise.
            // Not a reset: that would drop LastRunAt, and the digest would go out again right away.
            var flowId = FlowHub.NewId<DigestFlow>(account.Id.Value);
            context.Operation.AddEvent(FlowHub.NewResumeEvent(flowId));
        }
    }

    // [CommandHandler]
    public virtual async Task OnDelete(AccountsBackend_Delete command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();

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
        await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.CoachDays
            .Where(d => d.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // The row stays as a tombstone so the id can never be reused, stripped of everything that
        // identified its owner. The authors stay too: each chat's cleanup needs them to find the
        // messages, and removes them once it has.
        var dbAccount = await dbContext.Accounts
            .FirstOrDefaultAsync(a => a.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbAccount is not null) {
            dbAccount.Status = AccountStatus.Removed;
            dbAccount.Email = "";
            dbAccount.IsEmailVerified = false;
            dbAccount.Phone = "";
            dbAccount.Name = "";
            dbAccount.TimeZone = "";
            dbAccount.AliasId = "";
            dbAccount.Claims = ImmutableDictionary<string, string>.Empty;
            dbAccount.Version = VersionGenerator.NextVersion(dbAccount.Version);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Invalidation.Defer(() => {
            _ = Get(account.Id, default);
            foreach (var (identity, _) in account.Identities)
                _ = GetIdByUserIdentity(identity, default);
            if (account.AliasId is { } aliasId)
                _ = GetIdByAlias(aliasId, default);
            _ = ListSessions(account.Id, default);
        });

        context.Operation.AddEvent(new AccountChangedEvent(account, account, ChangeKind.Remove));
    }

    // Event handlers

    // [EventHandler]
    public virtual Task OnNewAccountEvent(NewAccountEvent eventCommand, CancellationToken cancellationToken)
    {
        ContactGreeter.Activate();
        return Task.CompletedTask;
    }

    // Protected/internal methods

    internal bool IsAdmin(AccountFull account)
        => IsAdmin(account,
            // Excluding integration-test hosts, which are loopback too: a test agent must not
            // become an admin there just by signing in.
            HostInfo.IsLocalDevInstance() && !HostInfo.IsTested,
            UsersSettings.PredefinedTotps,
            UsersSettings.PredefinedEmailTotps);

    internal static bool IsAdmin(
        AccountFull account,
        bool areTestAgentsAdmins,
        IReadOnlyDictionary<string, int> predefinedTotps,
        IReadOnlyDictionary<string, int> predefinedEmailTotps)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (account.Identities.HasInternalIdentity() && account.Id == Constants.User.Admin.UserId)
            return true;

        // Phones with a predefined TOTP belong to Apple/Google app review, and test-agent phones
        // sign in with a fixed code, so neither is ever an admin
        foreach (var phone in account.Identities.GetPhones()) {
            if (predefinedTotps.ContainsKey(ActualChat.Phone.NormalizePart(phone.Value))
                || Constants.Auth.TestAgent.IsTestAgentPhone(phone.Value))
                return false;
        }

        var emails = account.Identities.GetEmails();
        // Emails with a predefined TOTP sign in with a shared code, so they're never admins either,
        // even though they're on the team email domain
        foreach (var email in emails) {
            if (EmailAuth.GetPredefinedTotpPrefix(predefinedEmailTotps, email) is not null)
                return false;
        }

        foreach (var email in emails) {
            if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
                continue;

            // test-*@actual.chat matches the team email domain, so it's decided separately
            if (Constants.Auth.TestAgent.IsTestAgentEmail(email)) {
                if (areTestAgentsAdmins)
                    return true;

                continue;
            }

            if (AdminEmails.Contains(email))
                return true; // Predefined admin email
            if (emailAddress.Host == AdminEmailDomain)
                return true; // company email
        }
        return false;
    }

    // Private methods

    private async Task RecordSignInUsage(
        CommandContext context, Session session, UserId userId, bool isSignUp, SessionInfoFull? sessionInfo,
        CancellationToken cancellationToken)
    {
        // Measurement only: whatever goes wrong here must not fail the sign-in.
        // Everything goes out as operation events, so it counts only a committed sign-in and survives a retry.
        try {
            var arrivalKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.ArrivalKey);
            var signInFromLinkKey = Constants.SessionTemporals.ToClientKey(Constants.SessionTemporals.SignInFromLinkKey);
            var arrivalValue = await SessionTemporalsBackend.Get(session, arrivalKey, cancellationToken)
                .ConfigureAwait(false);
            var signInFromLinkValue = await SessionTemporalsBackend.Get(session, signInFromLinkKey, cancellationToken)
                .ConfigureAwait(false);
            if (isSignUp) {
                AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind);
                if (!ArrivalInfo.TryParse(arrivalValue, out var arrival))
                    arrival = ArrivalInfo.Fallback(appKind);

                var signUp = UsageEventSource.SignUp(arrival, Clocks.SystemClock.Now);
                context.Operation.AddEvent(new UsageBackend_Record(userId, ApiArray.New(signUp)));
                context.Operation.AddEvent(new UsageBackend_CountFunnelEvent(
                    userId, FunnelEvent.SignUp, session, arrival.Kind));
            }
            if (signInFromLinkValue is not null) {
                context.Operation.AddEvent(new UsageBackend_CountFunnelEvent(
                    userId, FunnelEvent.SignInCompletedFromLink, session));
                context.Operation.AddEvent(new SessionTemporalsBackend_Set(session, signInFromLinkKey, null));
            }
            // Any sign-in consumes the arrival: a later sign-up in this session is a different person's
            if (arrivalValue is not null)
                context.Operation.AddEvent(new SessionTemporalsBackend_Set(session, arrivalKey, null));
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to record the sign-in usage of user '{UserId}'", userId);
        }
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
        ApiMap<UserIdentity, string> originalIdentities,
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

            // account comes from a compute method, so it can predate a removal (passkeys do that) - only
            // the identities this sign-in brings in are created here, for the rest the DB decides.
            if (originalIdentities.ContainsKey(userIdentity))
                continue;

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
        var userId = account.Id?.Value.NullIfEmpty();
        if (userId is not null) {
            // A caller-supplied Id is taken as is - only the lock is needed
            await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);
        }
        else {
            userId = await ReserveUserId(dbContext, cancellationToken).ConfigureAwait(false);
        }

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

    private static async Task<string> ReserveUserId(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        // UserId is 6 random chars: ~8% odds of a collision by 100K accounts.
        // The lock precedes the probe, so a concurrent creator of the same Id waits rather than races.
        for (var i = 0; i < ReserveUserIdAttemptCount; i++) {
            var userId = UserId.New().Value;
            await dbContext.Accounts.Lock(userId, cancellationToken).ConfigureAwait(false);
            var isUsed = await dbContext.Accounts
                .AnyAsync(x => x.Id == userId, cancellationToken)
                .ConfigureAwait(false);
            if (!isUsed)
                return userId;
        }

        throw StandardError.Internal("Couldn't generate an unused UserId.");
    }
}
