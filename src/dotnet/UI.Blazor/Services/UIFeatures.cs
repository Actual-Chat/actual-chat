using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public static class UIFeatures
{
    public class EnableIncompleteUI : FeatureDef<bool>, IClientFeatureDef
    {
        public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        {
            var blazorUISettings = services.GetRequiredService<BlazorUISettings>();
            if (blazorUISettings.EnableIncompleteUI is { } enableIncompleteUI)
                return enableIncompleteUI;

            var hostInfo = services.GetRequiredService<HostInfo>();
            if (!hostInfo.IsDevelopmentInstance)
                return false;

            var session = services.GetRequiredService<Session>();
            var accounts = services.GetRequiredService<IAccounts>();
            var account = await accounts.Get(session, cancellationToken).ConfigureAwait(false);
            return account?.IsAdmin == true;
        }
    }
}
