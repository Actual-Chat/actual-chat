namespace ActualChat.UI.Blazor.Services;

public abstract class ExperimentalFeature : FeatureDef<bool>, IClientFeatureDef
{
    private static readonly HashSet<string> FocusGroupEmails = new() { "grigory.yakushev@gmail.com" };

    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return false;

        if (!IsTargetUser(account))
            return false;

        var accountSettings = services.AccountSettings(session);
        var appSettings = await accountSettings.UserAppSettings().Get(cancellationToken).ConfigureAwait(false);
        return appSettings.AreExperimentalFeaturesEnabled ?? true;
    }

    private static bool IsTargetUser(AccountFull account)
    {
        if (account.IsAdmin)
            return true;

        var emails = account.Identities.GetEmails();
        return emails.Any(email => FocusGroupEmails.Contains(email));
    }
}
