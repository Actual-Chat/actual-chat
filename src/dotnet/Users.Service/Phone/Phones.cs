using ActualChat.Geo;
using PhoneNumbers;

namespace ActualChat.Users.Phone;

public class Phones(IAccounts accounts) : IPhones
{
    // [ComputeMethod]
    public virtual Task<ActualChat.Phone?> Parse(string phone, CancellationToken cancellationToken)
        => Task.FromResult(PhoneExt.ParseNullable(phone, null));

    // [ComputeMethod]
    public virtual async Task<ActualChat.Phone?> GetExampleCountryPhone(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var phonePrefix = await GeoIP.ToPhonePrefix(sessionInfo?.IPAddress ?? "").ConfigureAwait(false);
        return PhoneExt.GetExampleCountryPhone(phonePrefix, 1);
    }
}
