using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class Features_EnableVideoStreaming : FeatureDef<bool>, IClientFeatureDef
{
    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var hostInfo = services.HostInfo();
        if (hostInfo.BaseUrlKind == BaseUrlKind.Production)
            return false;

        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return false;

        return await services.UserSettingsUI(session)
            .UserAppSettings()
            .Get(x => x.IsVideoStreamingEnabled ?? false, cancellationToken)
            .ConfigureAwait(false);
    }
}
