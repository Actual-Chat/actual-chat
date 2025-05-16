// Phone helpers depending on LibPhoneNumbers.
// !!! The identical copies file must be located in:
// - Core.Server project root
// - App.Maui project root (might be a symlink to Core.Server's file)

// ReSharper disable once CheckNamespace
namespace PhoneNumbers;

public static class PhoneExt
{
    public static Phone? TryParse(string source, string? region)
        => TryParse(source, region, out var phone) ? phone : null;

    public static bool TryParse(string source, string? region, [NotNullWhen(true)] out Phone? phone)
    {
        if (PhoneNumberExt.TryParse(source, region, out var phoneNumber)) {
            phone = phoneNumber.ToPhone();
            return true;
        }

        phone = null;
        return false;
    }
}
