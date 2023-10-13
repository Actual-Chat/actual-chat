using ActualChat.Users;
using ILinkPreviews = ActualChat.Media.ILinkPreviews;

namespace ActualChat.UI.Blazor.Services;

public class Features_EnableLinkPreview : FeatureDef<bool>, IClientFeatureDef
{
    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var linkPreviews = services.GetRequiredService<ILinkPreviews>();
        if (await linkPreviews.IsEnabled().ConfigureAwait(false))
            return true;

        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsAdmin;
    }
}
