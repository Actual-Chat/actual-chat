using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class Features_EnableIncompleteUI : FeatureDef<bool>, IClientFeatureDef
{
    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var urlMapper = services.UrlMapper();
        if (urlMapper.IsLocalActualChat)
            return true;

        if (urlMapper.IsActualChat)
            return false;

        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsAdmin;
    }
}
