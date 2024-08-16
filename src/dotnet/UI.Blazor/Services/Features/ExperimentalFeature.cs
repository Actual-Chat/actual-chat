using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public abstract class ExperimentalFeature : FeatureDef<bool>, IClientFeatureDef
{
    private static readonly HashSet<string> FocusGroupEmails = new(StringComparer.Ordinal) { "grigory.yakushev@gmail.com" };

    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return false;

        if (!IsTargetUser(account))
            return false;

        var accountSettings = services.AccountSettings();
        var appSettings = await accountSettings.GetUserAppSettings(cancellationToken).ConfigureAwait(false);
        return appSettings.AreExperimentalFeaturesEnabled ?? true;
    }

    private static bool IsTargetUser(AccountFull account)
    {
        if (account.IsAdmin)
            return true;

        var email = account.GetVerifiedEmail();
        return !email.IsNullOrEmpty() && FocusGroupEmails.Contains(email);
    }
}
