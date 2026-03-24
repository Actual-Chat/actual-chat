using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class Features_EnableVideoStreaming : FeatureDef<bool>, IClientFeatureDef
{
    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            return false;

        return await services.UserSettingsUI(session)
            .UserAppSettings()
            .Get(x => x.IsVideoStreamingEnabled ?? false, cancellationToken)
            .ConfigureAwait(false);
    }
}
