namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class TestFeature_ClientAccount : FeatureDef<AccountFull?>, IClientFeatureDef
{
    public override async Task<AccountFull?> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsGuest ? null : account;
    }
}
