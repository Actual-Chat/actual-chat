using ActualChat.Geo;
using ActualChat.Users.Module;
using PhoneNumbers;

namespace ActualChat.Users.Phone;

public class Phones(IAccounts accounts, UsersSettings settings) : IPhones
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

        // 1. Predefined test numbers (e.g. app-review accounts) must resolve exactly,
        // no matter which country the caller's IP maps to
        var digits = ActualChat.Phone.NormalizePart(phone);
        if (settings.PredefinedTotps.ContainsKey(digits)) {
            var predefined = PhoneExt.ParseNullable("+" + digits, null);
            if (predefined != null)
                return predefined;
        }

        // 2. Try as-is (works when input has country code, e.g. "+1 (201) 555-0123")
        var result = PhoneExt.ParseNullable(phone, null);
        if (result != null)
            return result;

        // 3. Two candidate interpretations: a national number in the GeoIP country
        // (e.g. "(201) 555-0123") and an international number typed without '+'
        // (e.g. "12015550123"). A valid one beats a merely possible one; GeoIP wins ties.
        var util = PhoneNumberUtil.GetInstance();
        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var countryCode = await GeoIP.ToCountryCode(sessionInfo?.IPAddress ?? "").ConfigureAwait(false);
        PhoneNumber? geoIpNumber = null;
        if (!countryCode.IsNullOrEmpty())
            PhoneFormatterExt.TryParse(util, phone, countryCode, out geoIpNumber);
        PhoneNumber? plusNumber = null;
        if (!phone.StartsWith('+'))
            PhoneFormatterExt.TryParse(util, "+" + phone, null, out plusNumber);

        if (geoIpNumber != null && util.IsValidNumber(geoIpNumber))
            return geoIpNumber.ToPhone();
        if (plusNumber != null && util.IsValidNumber(plusNumber))
            return plusNumber.ToPhone();
        if (geoIpNumber != null && util.IsPossibleNumber(geoIpNumber))
            return geoIpNumber.ToPhone();
        if (plusNumber != null && util.IsPossibleNumber(plusNumber))
            return plusNumber.ToPhone();

        return null;
    }

    // [ComputeMethod]
    public virtual async Task<ActualChat.Phone?> GetExampleCountryPhone(Session session, CancellationToken cancellationToken)
    {
        var sessionInfo = await accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var phonePrefix = await GeoIP.ToPhonePrefix(sessionInfo?.IPAddress ?? "").ConfigureAwait(false);
        return PhoneExt.GetExampleCountryPhone(phonePrefix, 1);
    }
}
