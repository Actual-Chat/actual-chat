using PhoneNumbers;

namespace ActualChat.UI.Blazor.Services;

public class PhoneNumberExtractor(string? defaultRegion)
{
    private readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();

    public Phone GetFromNumber(string s)
        => !_phoneNumberUtil.TryParse(s, defaultRegion, out var phoneNumber)
            ? Phone.None
            : phoneNumber.CreatePhone();

    public static PhoneNumberExtractor CreateFor(string sOwnPhoneNumber)
    {
        string? defaultRegion = null;
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        if (!string.IsNullOrEmpty(sOwnPhoneNumber)
            && phoneNumberUtil.TryParse(sOwnPhoneNumber, null, out var phoneNumber))
            defaultRegion = phoneNumberUtil.GetRegionCodeForNumber(phoneNumber);
        return new PhoneNumberExtractor(defaultRegion);
    }
}
