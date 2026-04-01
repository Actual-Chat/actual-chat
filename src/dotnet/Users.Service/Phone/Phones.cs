using ActualChat.Geo;
using PhoneNumbers;

namespace ActualChat.Users.Phone;

public class Phones(IAccounts accounts) : IPhones
{
    // [ComputeMethod]
    public virtual Task<ActualChat.Phone?> Parse(string phone, CancellationToken cancellationToken)
        => Task.FromResult(PhoneExt.ParseNullable(phone, null));

    // [ComputeMethod]
    public virtual async Task<ActualChat.Phone?> ParseWithCountryFallback(
        Session session, string phone, CancellationToken cancellationToken)
    {
        if (phone.IsNullOrEmpty())
            return null;

        // 1. Try as-is (works when input has country code, e.g. "+1 (201) 555-0123")
        var result = PhoneExt.ParseNullable(phone, null);
        if (result != null)
            return result;

        // 2. Try with user's detected region from GeoIP (handles national numbers like "(201) 555-0123")
        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var countryCode = await GeoIP.ToCountryCode(sessionInfo?.IPAddress ?? "").ConfigureAwait(false);
        if (!countryCode.IsNullOrEmpty()) {
            result = PhoneExt.ParseNullable(phone, countryCode);
            if (result != null)
                return result;
        }

        // 3. Try with '+' prefix (user typed international number without '+', e.g. "12015550123")
        if (!phone.StartsWith('+'))
            result = PhoneExt.ParseNullable("+" + phone, null);

        return result;
    }

    // [ComputeMethod]
    public virtual async Task<ActualChat.Phone?> GetExampleCountryPhone(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var phonePrefix = await GeoIP.ToPhonePrefix(sessionInfo?.IPAddress ?? "").ConfigureAwait(false);
        return PhoneExt.GetExampleCountryPhone(phonePrefix, 1);
    }
}
