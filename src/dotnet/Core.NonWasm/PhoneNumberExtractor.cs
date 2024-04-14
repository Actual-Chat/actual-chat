using PhoneNumbers;

namespace ActualChat.Core.NonWasm;

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
        if (!sOwnPhoneNumber.IsNullOrEmpty()
            && phoneNumberUtil.TryParse(sOwnPhoneNumber, null, out var phoneNumber))
            defaultRegion = phoneNumberUtil.GetRegionCodeForNumber(phoneNumber);
        return new PhoneNumberExtractor(defaultRegion);
    }
}
