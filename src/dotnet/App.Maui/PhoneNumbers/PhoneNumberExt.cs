// Phone helpers depending on LibPhoneNumbers.
// !!! The identical copies file must be located in:
// - Core.Server project root
// - App.Maui project root (might be a symlink to Core.Server's file)

// ReSharper disable once CheckNamespace
namespace PhoneNumbers;

public static class PhoneNumberExt
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
