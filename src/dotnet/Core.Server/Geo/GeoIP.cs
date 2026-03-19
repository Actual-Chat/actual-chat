using System.Net;
using PhoneNumbers;

namespace ActualChat.Geo;

public static class GeoIP
{
    private static readonly PhoneNumberUtil PhoneNumberUtil = PhoneNumberUtil.GetInstance();

    public static async Task<string?> ToCountryCode(IPAddress ipAddress)
    {
        await GeoLiteDb.WhenReady.ConfigureAwait(false);
        try {
            var response = GeoLiteDb.Reader.Country(ipAddress);
            return response.Country.IsoCode;
        }
        catch (Exception) {
            return null;
        }
    }

    public static async Task<int> ToPhonePrefix(IPAddress ipAddress)
    {
        var countryCode = await ToCountryCode(ipAddress).ConfigureAwait(false);
        if (countryCode.IsNullOrEmpty())
            return 0;

        var prefix = PhoneNumberUtil.GetCountryCodeForRegion(countryCode);
        return prefix;
    }
}
