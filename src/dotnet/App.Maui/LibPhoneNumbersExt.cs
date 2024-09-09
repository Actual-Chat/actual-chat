using System.Diagnostics.CodeAnalysis;

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

    public Phone Parse(string source)
        => PhoneNumberExt.TryParse(PhoneNumberUtil, source, Region, out var phoneNumber)
            ? phoneNumber.ToPhone()
            : Phone.None;

    public static PhoneParser ForRegion(string? region, PhoneNumberUtil? phoneNumberUtil = null)
        => new(region, phoneNumberUtil);

    public static PhoneParser ForOwnPhone(string ownPhoneNumber, PhoneNumberUtil? phoneNumberUtil = null)
    {
        string? defaultRegion = null;
        if (!ownPhoneNumber.IsNullOrEmpty() && PhoneNumberExt.TryParse(ownPhoneNumber, null, out var phoneNumber))
            defaultRegion = PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber);
        return new PhoneParser(defaultRegion, phoneNumberUtil);
    }

    private PhoneParser(string? region, PhoneNumberUtil? phoneNumberUtil = null)
    {
        PhoneNumberUtil = phoneNumberUtil ?? PhoneNumberUtil.GetInstance();
        Region = region;
    }
}

public static class PhoneNumberExt
{
    public static Phone ToPhone(this PhoneNumber phoneNumber)
        => new(
            phoneNumber.CountryCode.Format(),
            phoneNumber.NationalNumber.Format(),
            AssumeValid.Option);

    public static bool TryParse(string source, string? region, [NotNullWhen(true)] out PhoneNumber? phoneNumber)
        => TryParse(PhoneNumberUtil.GetInstance(), source, region, out phoneNumber);
    public static bool TryParse(
        PhoneNumberUtil phoneNumberUtil, string source, string? region,
        [NotNullWhen(true)] out PhoneNumber? phoneNumber)
    {
        try {
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
    public static Phone Parse(string source, string? region)
        => TryParse(source, region, out var phone) ? phone : default;

    public static bool TryParse(string source, string? region, out Phone phone)
    {
        if (PhoneNumberExt.TryParse(source, region, out var phoneNumber)) {
            phone = phoneNumber.ToPhone();
            return true;
        }

        phone = default;
        return false;
    }
}
