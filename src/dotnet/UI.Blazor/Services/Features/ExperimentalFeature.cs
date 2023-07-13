using ActualChat.Hosting;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public abstract class ExperimentalFeature : FeatureDef<bool>, IClientFeatureDef
{
    private static readonly HashSet<string> FocusGroupEmails = new(StringComparer.Ordinal) { "grigory.yakushev@gmail.com" };

    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var hostInfo = services.GetRequiredService<HostInfo>();
        if (hostInfo.IsDevelopmentInstance)
            return true;

        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return false;

        if (account.IsAdmin)
            return true;

        var email = account.User.GetEmail();
        return !email.IsNullOrEmpty() && FocusGroupEmails.Contains(email);
    }
}
