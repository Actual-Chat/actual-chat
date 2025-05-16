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

    private PhoneParser(string? region, PhoneNumberUtil? phoneNumberUtil = null)
    {
        PhoneNumberUtil = phoneNumberUtil ?? PhoneNumberUtil.GetInstance();
        Region = region;
    }

    public Phone? TryParse(string source)
        => PhoneNumberExt.TryParse(PhoneNumberUtil, source, Region, out var phoneNumber)
            ? phoneNumber.ToPhone()
            : null;

    public static PhoneParser ForRegion(string? region, PhoneNumberUtil? phoneNumberUtil = null)
        => new(region, phoneNumberUtil);

    public static PhoneParser ForOwnPhone(string ownPhoneNumber, PhoneNumberUtil? phoneNumberUtil = null)
    {
        string? defaultRegion = null;
        if (!ownPhoneNumber.IsNullOrEmpty() && PhoneNumberExt.TryParse(ownPhoneNumber, null, out var phoneNumber))
            defaultRegion = PhoneNumberUtil.GetInstance().GetRegionCodeForNumber(phoneNumber);
        return new PhoneParser(defaultRegion, phoneNumberUtil);
    }
}
