// Phone helpers depending on LibPhoneNumbers.
// !!! The identical copies file must be located in:
// - Core.Server project root
// - App.Maui project root (might be a symlink to Core.Server's file)

// ReSharper disable once CheckNamespace
namespace PhoneNumbers;

public class PhoneParser
{
    public PhoneNumberUtil PhoneNumberUtil { get; }
    public string? Region { get; }

    public Phone? ParseNullable(string source)
        => PhoneFormatterExt.TryParse(PhoneNumberUtil, source, Region, out var phoneNumber)
            ? phoneNumber.ToPhone()
            : null;

    public static PhoneParser ForRegion(string? region, PhoneNumberUtil? phoneNumberUtil = null)
        => new(region, phoneNumberUtil);

    public static PhoneParser ForOwnPhone(string ownPhoneNumber, PhoneNumberUtil? phoneNumberUtil = null)
    {
        string? defaultRegion = null;
        if (!ownPhoneNumber.IsNullOrEmpty() && PhoneFormatterExt.TryParse(ownPhoneNumber, null, out var phoneNumber))
            defaultRegion = PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber);
        return new PhoneParser(defaultRegion, phoneNumberUtil);
    }

    private PhoneParser(string? region, PhoneNumberUtil? phoneNumberUtil = null)
    {
        PhoneNumberUtil = phoneNumberUtil ?? PhoneNumberUtil.GetInstance();
        Region = region;
    }
}

public static class PhoneFormatterExt
{
    private static readonly HashSet<char> AllowedStartChars = ['+', '(', '-'];

    public static Phone ToPhone(this PhoneNumber phoneNumber)
        => Phone.New(
            phoneNumber.CountryCode.Format(),
            phoneNumber.NationalNumber.Format());

    public static bool TryParse(string source, string? region, [NotNullWhen(true)] out PhoneNumber? phoneNumber)
        => TryParse(PhoneNumberUtil.GetInstance(), source, region, out phoneNumber);
    public static bool TryParse(
        PhoneNumberUtil phoneNumberUtil, string source, string? region,
        [NotNullWhen(true)] out PhoneNumber? phoneNumber)
    {
        try {
            var firstChar = source.Trim().FirstOrDefault();
            if (!char.IsDigit(firstChar) && !AllowedStartChars.Contains(firstChar)) {
                phoneNumber = null;
                return false;
            }
            phoneNumber = phoneNumberUtil.Parse(source, region);
            return true;
        }
        catch (Exception) {
            phoneNumber = null;
            return false;
        }
    }
}

public static class PhoneExt
{
    public static Phone? GetExample(int prefix, int defaultPrefix = 0)
    {
        var util = PhoneNumberUtil.GetInstance();
        if (prefix <= 0 || util.GetRegionCodeForCountryCode(prefix) == "ZZ") {
            if (defaultPrefix == 0)
                return null;

            prefix = defaultPrefix;
        }

        var region = util.GetRegionCodeForCountryCode(prefix);
        var example = util.GetExampleNumber(region);
        return example.ToPhone();
    }

    public static Phone? ParseNullable(string source, string? region)
        => TryParse(source, region, out var phone) ? phone : null;

    public static bool TryParse(string source, string? region, [NotNullWhen(true)] out Phone? phone)
    {
        if (PhoneFormatterExt.TryParse(source, region, out var phoneNumber)) {
            phone = phoneNumber.ToPhone();
            return true;
        }

        phone = null;
        return false;
    }
}
