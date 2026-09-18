using ActualChat.WebHooks;

namespace ActualChat.Chat;

public class WebHooks(IServiceProvider services) : IWebHooks
{
    private IWebHooksBackend Backend { get; } = services.GetRequiredService<IWebHooksBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<WebHook?> Get(Session session, WebHookId id, CancellationToken cancellationToken)
    {
        var webHook = await Backend.Get(id, cancellationToken).ConfigureAwait(false);
        if (webHook is null)
            return null;

        var manager = await TryGetManager(session, webHook.Scope, webHook.ScopeId, cancellationToken)
            .ConfigureAwait(false);
        return manager is null ? null : webHook;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHook>> List(
        Session session,
        WebHookScope scope,
        string scopeId,
        CancellationToken cancellationToken)
    {
        await RequireManager(session, scope, scopeId, cancellationToken).ConfigureAwait(false);
        return await Backend.ListByScope(scope, scopeId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHookDelivery>> ListDeliveries(
        Session session,
        WebHookId id,
        CancellationToken cancellationToken)
    {
        var webHook = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        await RequireManager(session, webHook.Scope, webHook.ScopeId, cancellationToken).ConfigureAwait(false);
        return await Backend.ListDeliveries(id, Constants.WebHooks.DeliveryListLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<WebHookChangeResult> OnChange(
        WebHooks_Change command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!; // It just spawns other commands, so nothing to do here

        var (session, scope, scopeId, id, change) =
            (command.Session, command.Scope, command.ScopeId, command.Id, command.Change);
        change.RequireValid();
        if (!change.IsCreate(out _)) {
            var existing = await Backend.Get(id!, cancellationToken).Require().ConfigureAwait(false);
            if (existing.Scope != scope || existing.ScopeId != scopeId)
                throw StandardError.Constraint("Scope mismatch.");
        }

        var account = await RequireManager(session, scope, scopeId, cancellationToken).ConfigureAwait(false);
        var backendCommand =
            new WebHooksBackend_Change(scope, scopeId, id, command.ExpectedVersion, change, account.Id);
        return await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<string> OnRotateSecret(
        WebHooks_RotateSecret command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, id) = (command.Session, command.Id);
        var webHook = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        await RequireManager(session, webHook.Scope, webHook.ScopeId, cancellationToken).ConfigureAwait(false);
        var backendCommand = new WebHooksBackend_RotateSecret(id, webHook.ScopeId);
        return await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<WebHookTestResult> OnTest(WebHooks_Test command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, id) = (command.Session, command.Id);
        var webHook = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        var account = await RequireManager(session, webHook.Scope, webHook.ScopeId, cancellationToken)
            .ConfigureAwait(false);
        var backendCommand = new WebHooksBackend_Test(id, webHook.ScopeId, account.Avatar.Name);
        return await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRedeliver(WebHooks_Redeliver command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, id, deliveryId) = (command.Session, command.Id, command.DeliveryId);
        var webHook = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        await RequireManager(session, webHook.Scope, webHook.ScopeId, cancellationToken).ConfigureAwait(false);
        var backendCommand = new WebHooksBackend_Redeliver(id, webHook.ScopeId, deliveryId);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<AccountFull?> TryGetManager(
        Session session,
        WebHookScope scope,
        string scopeId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!AccountFull.MustBeActive.IsSatisfied(account))
            return null;

        switch (scope) {
        case WebHookScope.Chat:
            var chatRules = await Chats.GetRules(session, ChatId.Parse(scopeId), cancellationToken)
                .ConfigureAwait(false);
            return chatRules.CanModerate() ? account : null;
        case WebHookScope.Place:
            var placeRules = await Places.GetRules(session, PlaceId.Parse(scopeId), cancellationToken)
                .ConfigureAwait(false);
            return placeRules.IsOwner() ? account : null;
        default:
            return account.Id.Value == scopeId ? account : null;
        }
    }

    private async Task<AccountFull> RequireManager(
        Session session,
        WebHookScope scope,
        string scopeId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        switch (scope) {
        case WebHookScope.Chat:
            var chatRules = await Chats.GetRules(session, ChatId.Parse(scopeId), cancellationToken)
                .ConfigureAwait(false);
            chatRules.Require(ChatPermissions.Moderate);
            break;
        case WebHookScope.Place:
            var placeRules = await Places.GetRules(session, PlaceId.Parse(scopeId), cancellationToken)
                .ConfigureAwait(false);
            if (!placeRules.IsOwner())
                throw StandardError.Unauthorized("Only place owners can manage its integrations.");
            break;
        default:
            if (account.Id.Value != scopeId)
                throw StandardError.Unauthorized("You can manage only your own web hooks.");
            break;
        }
        return account;
    }
}
