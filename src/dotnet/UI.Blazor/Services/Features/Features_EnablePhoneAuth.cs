using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class Features_EnablePhoneAuth : FeatureDef<bool>, IClientFeatureDef
{
    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var hostInfo = services.GetRequiredService<HostInfo>();
        if (hostInfo.IsDevelopmentInstance)
            return true;

        var phoneAuth = services.GetRequiredService<IPhoneAuth>();
        var isEnabled = await phoneAuth.IsEnabled(cancellationToken).ConfigureAwait(false);
        return isEnabled;
    }
}
