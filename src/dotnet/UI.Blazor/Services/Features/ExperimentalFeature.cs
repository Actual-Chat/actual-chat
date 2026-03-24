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

        var userAppSettings = await services.UserSettingsUI(session)
            .UserAppSettings().Get(cancellationToken)
            .ConfigureAwait(false);
        return userAppSettings.AreExperimentalFeaturesEnabled ?? true;
    }

    private static bool IsTargetUser(AccountFull account)
    {
        if (account.IsAdmin)
            return true;

        var emails = account.Identities.GetEmails();
        return emails.Any(email => FocusGroupEmails.Contains(email));
    }
}
