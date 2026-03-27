using PhoneNumbers;

namespace ActualChat.Geo;

public static class GeoIP
{
    private static readonly PhoneNumberUtil PhoneNumberUtil = PhoneNumberUtil.GetInstance();

    public static async ValueTask<string?> ToCountryCode(string ipAddress)
    {
        var reader = await GeoLiteDb.WhenCountryDbReady.ConfigureAwait(false);
        try {
            if (!reader.TryCountry(ipAddress, out var response) || response is null)
                return null;

            return response.Country.IsoCode;
        }
        catch (Exception) {
            return null;
        }
    }

    public static async ValueTask<string?> ToCountryName(string ipAddress)
    {
        var reader = await GeoLiteDb.WhenCountryDbReady.ConfigureAwait(false);
        try {
            if (!reader.TryCountry(ipAddress, out var response) || response is null)
                return null;

            return response.Country.Name;
        }
        catch (Exception) {
            return null;
        }
    }

    public static async ValueTask<string?> ToCityAndCountryName(string ipAddress)
    {
        var reader = await GeoLiteDb.WhenCityDbReady.ConfigureAwait(false);
        try {
            if (!reader.TryCity(ipAddress, out var response) || response is null)
                return null;
            if (response.City.Name.IsNullOrEmpty())
                return null;

            return $"{response.City.Name}, {response.Country.Name}";
        }
        catch (Exception) {
            return null;
        }
    }

    public static async ValueTask<int> ToPhonePrefix(string ipAddress)
    {
        var countryCode = await ToCountryCode(ipAddress).ConfigureAwait(false);
        if (countryCode.IsNullOrEmpty())
            return 0;

        var prefix = PhoneNumberUtil.GetCountryCodeForRegion(countryCode);
        return prefix;
    }
}
