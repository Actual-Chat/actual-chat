using System.Diagnostics.CodeAnalysis;
using PhoneNumbers;

namespace ActualChat.UI.Blazor.Services;

public static class PhoneNumberUtilExt
{
    public static bool TryParse(
        this PhoneNumberUtil util,
        string numberToParse,
        string? defaultRegion,
        [NotNullWhen(true)] out PhoneNumber? phoneNumber)
    {
        try {
            phoneNumber = util.Parse(numberToParse, defaultRegion);
            return true;
        }
        catch (Exception) {
            phoneNumber = null;
            return false;
        }
    }

    public static Phone CreatePhone(this PhoneNumber phoneNumber)
        => new Phone(
            phoneNumber.CountryCode.Format(),
            phoneNumber.NationalNumber.Format(),
            AssumeValid.Option);
}
