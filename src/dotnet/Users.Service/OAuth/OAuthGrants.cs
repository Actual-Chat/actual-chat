namespace ActualChat.OAuth;

public class OAuthGrants(IServiceProvider services) : IOAuthGrants
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IOAuthGrantsBackend Backend { get; } = services.GetRequiredService<IOAuthGrantsBackend>();

    public virtual async Task<ApiArray<OAuthGrant>> List(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return [];

        return await Backend.List(account.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task<OAuthClientInfo?> GetClient(
        Session session, string clientId, CancellationToken cancellationToken)
        => Backend.GetClient(clientId, cancellationToken);

    public virtual async Task<string> OnApprove(OAuthGrants_Approve command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        account.Require(AccountFull.MustBeActive);
        return await Backend.Approve(account.Id, command.ClientId, command.Scopes, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task OnRevoke(OAuthGrants_Revoke command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        await Backend.Revoke(account.Id, command.AuthorizationId, cancellationToken).ConfigureAwait(false);
    }
}
