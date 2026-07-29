using ActualChat.Contacts;
using ActualChat.Geo;
using ActualChat.Logging;
using ActualChat.Notifications;
using ActualChat.Security;
using UAParser;

namespace ActualChat.Users;

/// <summary>
/// Frontend service for user account operations with session-based access control.
/// </summary>
public class Accounts(IServiceProvider services) : IAccounts
{
    // ReSharper disable once InconsistentNaming
    private static readonly Parser UAParser = Parser.GetDefault();

    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private IAccountsBackend Backend { get; } = services.GetRequiredService<IAccountsBackend>();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<Accounts>();

    // Compute methods

    // [ComputeMethod]
    public virtual async Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        UserId userId;
        if (sessionInfo is { IsActive: true, UserId: { } vUserId })
            userId = vUserId;
        else {
            if (sessionInfo?.GetGuestId() is not { } guestId) {
                Log.LogWarning("Session {SessionId} is missing or has no GuestId, using a temporary guest", session.Id);
                guestId = UserId.NewGuest();
            }
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

    // [ComputeMethod]
    public virtual Task<SessionInfoFull?> GetSessionInfo(Session session, CancellationToken cancellationToken)
        => SessionsBackend.Get(session, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<ApiList<SessionInfo>> ListOwnSessions(
        Session session, SessionKind kind, CancellationToken cancellationToken)
    {
        var ownAccount = await GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.IsGuest)
            return [];

        var userId = ownAccount.Id;
        var sessions = await Backend.ListSessions(userId, cancellationToken).ConfigureAwait(false);
        var result = new List<SessionInfo>();
        foreach (var s in sessions) {
            if (s.Kind != kind)
                continue;

            var sessionInfo = await SessionsBackend.Get(s, cancellationToken).ConfigureAwait(false);
            if (sessionInfo is null || !sessionInfo.IsActive)
                continue;

            var info = sessionInfo.ToSessionInfo()! with { IsCurrent = s == session };
            if (s.Kind is SessionKind.Session) {
                var userAgent = FormatUserAgent(info.Description);
                var location = await FormatLocation(info.IPAddress).ConfigureAwait(false);
                info = info with { Description = $"{userAgent} at {location}" };
            }
            result.Add(info);
        }
        return result.ToApiList();
    }

    // Command handlers

    // [CommandHandler]
    public virtual async Task OnSignOut(Accounts_SignOut command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var backendCommand = new AccountsBackend_SignOut(command.Session, command.Deactivate);
        await Commander.Call(backendCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, account, expectedVersion) = command;
        await this.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);

        // Clients send a whole AccountFull, but only the fields listed below are theirs to set.
        // Everything else - identities, claims, verification and audit state - is taken from
        // the stored account, so whatever the client sent for it is ignored rather than rejected.
        var existingAccount = await Backend.Get(account.Id, cancellationToken).Require().ConfigureAwait(false);
        account = existingAccount with {
            Status = account.Status,
            Name = account.Name,
            Email = account.Email,
            Phone = account.Phone,
            TimeZone = account.TimeZone,
            SyncContacts = account.SyncContacts,
            AliasId = account.AliasId,
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

        // AccountsBackend.OnDelete handles user_sessions cleanup
        var deleteOwnAccountCommand = new AccountsBackend_Delete(ownAccount.Id);
        await Commander.Call(deleteOwnAccountCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<string> OnCreateApiKey(Accounts_CreateApiKey command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return ""; // It just spawns other commands, so nothing to do here

        var maxDays = (int)CoreConstants.Session.MaxApiKeyExpirationTime.TotalDays;
        if (command.ExpiresInDays < 1 || command.ExpiresInDays > maxDays)
            throw StandardError.Constraint($"API key expiration must be between 1 and {maxDays} days.");

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustNotBeGuest);
        ownAccount.Require(AccountFull.MustBeActive);

        var apiKey = SessionExt.NewApiKey();
        var upsertCommand = new SessionsBackend_Upsert(apiKey) {
            ExpiresAt = Clocks.SystemClock.Now + TimeSpan.FromDays(command.ExpiresInDays),
            Description = command.Name,
            UserId = ownAccount.Id,
        };
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return apiKey.Id;
    }

    // [CommandHandler]
    public virtual async Task OnDeactivateSession(Accounts_DeactivateSession command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustNotBeGuest);

        var userId = ownAccount.Id;
        var sessions = await Backend.ListSessions(userId, cancellationToken).ConfigureAwait(false);
        var targetSession = sessions.FirstOrDefault(x => x.HasIdPrefix(command.IdPrefix));
        if (targetSession is null)
            throw StandardError.NotFound<Session>();
        if (targetSession == command.Session)
            throw StandardError.NotSupported("Cannot deactivate the current session.");

        var signOutCommand = new AccountsBackend_SignOut(targetSession, Deactivate: true);
        await Commander.Call(signOutCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnConfirmRegister(Accounts_ConfirmRegister command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, token) = command;
        session.RequireValid();

        var pending = PendingRegistrationExt.TryDecode(SecureTokensBackend, token, session)
            ?? throw StandardError.Constraint("This registration request has expired. Please try signing in again.");

        var signInCommand = new AccountsBackend_SignIn(
            pending.Session,
            pending.AuthenticatedIdentity,
            pending.Identities,
            pending.Claims,
            AutoCreate: true);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);

        // Clear the pending-registration prompt — OnSignIn already does this on success,
        // but call it here too in case the prompt key was somehow retained.
        var clearCmd = new SessionTemporalsBackend_Set(
            session, Constants.SessionTemporals.PendingRegistrationKey, null);
        await Commander.Call(clearCmd, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnCancelRegister(Accounts_CancelRegister command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, token) = command;
        session.RequireValid();

        // Requiring the token to decode for this session stops a stale prompt from cancelling a fresh one
        _ = PendingRegistrationExt.TryDecode(SecureTokensBackend, token, session)
            ?? throw StandardError.Constraint("This registration request has expired. Please try signing in again.");

        var clearCmd = new SessionTemporalsBackend_Set(
            session, Constants.SessionTemporals.PendingRegistrationKey, null);
        await Commander.Call(clearCmd, true, cancellationToken).ConfigureAwait(false);

        var setErrorCmd = new SessionTemporalsBackend_Set(
            session, Constants.SessionTemporals.SignInErrorKey, Constants.SessionTemporals.SignInCanceledMessage);
        await Commander.Call(setErrorCmd, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeactivateAllSessions(Accounts_DeactivateAllSessions command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        var userId = ownAccount.Id;
        var sessions = await Backend.ListSessions(userId, cancellationToken).ConfigureAwait(false);

        var kinds = command.Kinds.ToHashSet();
        foreach (var session in sessions) {
            if (session == command.Session || !kinds.Contains(session.Kind))
                continue;

            var signOutCommand = new AccountsBackend_SignOut(session, Deactivate: true);
            await Commander.Call(signOutCommand, true, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private static string FormatUserAgent(string description)
    {
        if (description.IsNullOrEmpty())
            return "Unknown";

        // Mobile app user agents: "Android/2.6.123 ...", "Ios/2.6.123 ...", etc.
        var app = TryParseApp(description);
        if (app is not null)
            return $"{CoreConstants.AppName} {app}";

        // Browser user agents: parse with UAParser
        try {
            var client = UAParser.Parse(description);
            var browser = client.UA.Family;
            var version = client.UA.Major;
            if (!browser.IsNullOrEmpty() && browser != "Other") {
                var result = browser;
                if (!version.IsNullOrEmpty())
                    result += $" v{version}";
                return result;
            }
        }
        catch {
            // Parsing failed, return as-is
        }
        return description;
    }

    private static async ValueTask<string> FormatLocation(string ipAddress)
    {
        if (ipAddress.IsNullOrEmpty())
            return "Unknown location";

        var location = await GeoIP.ToCityAndCountryName(ipAddress).ConfigureAwait(false);
        if (!location.IsNullOrEmpty())
            return location;

        location = await GeoIP.ToCountryName(ipAddress).ConfigureAwait(false);
        if (!location.IsNullOrEmpty())
            return location;

        return $"{ipAddress}, Unknown location";
    }

    private static string? TryParseApp(string description)
    {
        // Format: "{AppKind}/{version} {optional HTTP UserAgent}"
        var slashIndex = description.IndexOf('/');
        if (slashIndex <= 0)
            return null;

        var prefix = description[..slashIndex];
        return prefix switch {
            "AndroidApp" => "Android App",
            "IosApp" => "iOS App",
            "WindowsApp" => "Windows App",
            "MacOSApp" => "macOS App",
            "WasmApp" => "Web App",
            "UnknownApp" => "App",
            _ => null,
        };
    }
}
