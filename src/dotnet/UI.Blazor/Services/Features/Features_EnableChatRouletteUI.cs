using ActualChat.Roulette;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public class Features_EnableChatRouletteUI : FeatureDef<bool>, IClientFeatureDef
{
    private static readonly HashSet<string> FocusGroupEmails = new(StringComparer.Ordinal) { "grigory.yakushev@gmail.com" };

    public override async Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var session = services.Session();
        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return false;

        if (IsSpecialUser(account))
            return true;

        var roulette = services.GetRequiredService<IRoulette>();
        return await roulette.EnableChatRouletteUI(session, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSpecialUser(AccountFull account)
    {
        if (account.IsAdmin)
            return true;

        var email = account.GetVerifiedEmail();
        return !email.IsNullOrEmpty() && FocusGroupEmails.Contains(email);
    }
}
