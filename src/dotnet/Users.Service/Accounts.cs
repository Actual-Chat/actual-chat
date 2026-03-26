using ActualChat.Contacts;
using ActualChat.Notification;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

/// <summary>
/// Frontend service for user account operations with session-based access control.
/// </summary>
public class Accounts(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccounts
{
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private IAccountsBackend Backend { get; } = services.GetRequiredService<IAccountsBackend>();

    // [CommandHandler]
    public virtual async Task OnSignOut(Accounts_SignOut command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new AccountsBackend_SignOut(command.Session, command.Force);
        await Commander.Call(backendCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, account, expectedVersion) = command;
        await this.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);

        // Preserve Claims and Identities from existing account
        // Old apps don't have these properties, so they send empty values
        // which would otherwise wipe out the existing data
        var existingAccount = await Backend.Get(account.Id, cancellationToken).Require().ConfigureAwait(false);
        account = account with {
            Claims = existingAccount.Claims,
            Identities = existingAccount.Identities,
        };

        var updateCommand = new AccountsBackend_Update(account, expectedVersion);
        await Commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeleteOwn(Accounts_DeleteOwn command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        // NOTE(AY): This should go through the events / queues, let's discuss this.

        // Sign out to prevent unexpected UI invalidations
        var signOutCommand = new AccountsBackend_SignOut(command.Session);
        await Commander.Call(signOutCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteOwnChatsCommand = new ChatsBackend_RemoveOwnChats(ownAccount.Id);
        await Commander.Call(deleteOwnChatsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteOwnMessagesCommand = new ChatsBackend_RemoveOwnEntries(ownAccount.Id);
        await Commander.Call(deleteOwnMessagesCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteNotificationsCommand = new NotificationsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteNotificationsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteContactsCommand = new ContactsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteContactsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteExternalContactsCommand = new ExternalContactsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteExternalContactsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteExternalContactHashesCommand = new ExternalContactHashesBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteExternalContactHashesCommand, true, cancellationToken).ConfigureAwait(false);

        // Remove all user_sessions entries
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _2 = dbContext.ConfigureAwait(false);
        await dbContext.UserSessions
            .Where(x => x.UserId == ownAccount.Id.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var deleteOwnAccountCommand = new AccountsBackend_Delete(ownAccount.Id);
        await Commander.Call(deleteOwnAccountCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<string> OnCreateApiKey(Accounts_CreateApiKey command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return ""; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        var userId = ownAccount.Id;
        // Generate API key session ID with "api-" prefix
        var apiKeySessionId = CoreConstants.Session.ApiKeyPrefix + Session.New().Id;
        var apiKeySession = new Session(apiKeySessionId);

        // Create session via upsert with user info
        var upsertCommand = new SessionsBackend_Upsert(
            apiKeySession, "", "", default,
            userId.Value, ownAccount.Identities.FirstOrDefault().Key.Id ?? "");
        var sessionInfo = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);

        // Set Name and ExpiresAt on the DbSessionInfo directly
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await dbContext.Sessions
            .FirstOrDefaultAsync(s => s.Id == apiKeySessionId, cancellationToken)
            .ConfigureAwait(false);
        if (dbSessionInfo is not null) {
            dbSessionInfo.Name = command.Name;
            dbSessionInfo.ExpiresAt = command.ExpiresAt?.ToDateTime();
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Insert DbUserSession mapping
        dbContext.UserSessions.Add(new DbUserSession {
            UserId = userId.Value,
            SessionId = apiKeySessionId,
            IsApiKey = true,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return apiKeySessionId;
    }

    // [CommandHandler]
    public virtual async Task OnDeactivateSession(Accounts_DeactivateSession command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        var userId = ownAccount.Id;
        var backend = Services.GetRequiredService<IAccountsBackend>();
        var sessionIds = await backend.GetSessionIds(userId, cancellationToken).ConfigureAwait(false);

        var targetSessionId = sessionIds.FirstOrDefault(sid =>
            sid.Length >= CoreConstants.Session.IdPrefixLength
            && sid[..CoreConstants.Session.IdPrefixLength] == command.IdPrefix);

        if (targetSessionId is null)
            throw StandardError.NotFound<SessionInfo>("Session not found.");

        var signOutCommand = new AccountsBackend_SignOut(new Session(targetSessionId), true);
        await Commander.Call(signOutCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeactivateAllSessions(Accounts_DeactivateAllSessions command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        var userId = ownAccount.Id;
        var backend = Services.GetRequiredService<IAccountsBackend>();
        var sessionIds = await backend.GetSessionIds(userId, cancellationToken).ConfigureAwait(false);
        var currentSessionId = command.Session.Id;

        foreach (var sessionId in sessionIds) {
            // Skip current session unless ApiKeysOnly (in which case we only deactivate API keys)
            if (command.ApiKeysOnly) {
                if (!sessionId.StartsWith(CoreConstants.Session.ApiKeyPrefix, StringComparison.Ordinal))
                    continue;
            }
            else if (sessionId == currentSessionId)
                continue;

            var signOutCommand = new AccountsBackend_SignOut(new Session(sessionId), true);
            await Commander.Call(signOutCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual Task UpdatePresence(Session session, CancellationToken cancellationToken)
        => SessionsBackend.UpdatePresence(session, cancellationToken);

    // Compute methods

    // [ComputeMethod]
    public virtual Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken)
        => SessionsBackend.IsSignOutForced(session, cancellationToken);

    // [ComputeMethod]
    public virtual Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken)
        => SessionsBackend.Get(session, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<ApiList<UserSessionInfo>> GetOwnSessions(Session session, bool isApiKey, CancellationToken cancellationToken)
    {
        var ownAccount = await GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.IsGuest)
            return new ApiList<UserSessionInfo>();

        var userId = ownAccount.Id;
        var backend = Services.GetRequiredService<IAccountsBackend>();
        var sessionIds = await backend.GetSessionIds(userId, cancellationToken).ConfigureAwait(false);

        var result = new List<UserSessionInfo>();
        foreach (var sessionId in sessionIds) {
            var isSessionApiKey = sessionId.StartsWith(CoreConstants.Session.ApiKeyPrefix, StringComparison.Ordinal);
            if (isSessionApiKey != isApiKey)
                continue;

            var s = new Session(sessionId);
            var sessionInfo = await SessionsBackend.Get(s, cancellationToken).ConfigureAwait(false);
            if (sessionInfo is null)
                continue;

            // Read DbSessionInfo for Name and ExpiresAt
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _ = dbContext.ConfigureAwait(false);
            var dbSessionInfo = await dbContext.Sessions
                .FirstOrDefaultAsync(si => si.Id == sessionId, cancellationToken)
                .ConfigureAwait(false);

            var userSessionInfo = new UserSessionInfo(s.GetPrefix()) {
                IsApiKey = isSessionApiKey,
                IsActive = !sessionInfo.IsSignOutForced,
                Name = dbSessionInfo?.Name ?? "",
                UserAgent = sessionInfo.UserAgent,
                CreatedAt = sessionInfo.CreatedAt,
                LastSeenAt = sessionInfo.LastSeenAt,
                ExpiresAt = dbSessionInfo?.ExpiresAt is { } expiresAt ? new Moment(expiresAt) : null,
            };
            result.Add(userSessionInfo);
        }

        return result.ToApiList();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        UserId userId;
        if (sessionInfo?.IsAuthenticated() ?? false)
            userId = UserId.Parse(sessionInfo.UserId);
        else {
            if (sessionInfo?.GetGuestId() is not { } guestId)
                throw StandardError.Internal("Invalid session or GuestId is not set.");

            userId = guestId;
        }

        var account = await Backend.Get(userId, cancellationToken).Require().ConfigureAwait(false);
        return account;
    }

    // [ComputeMethod]
    public virtual async Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken)
    {
        var account = await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
        return account.ToAccount();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken)
    {
        var account = await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
        await this.AssertCanRead(session, account, cancellationToken).ConfigureAwait(false);
        return account;
    }
}
