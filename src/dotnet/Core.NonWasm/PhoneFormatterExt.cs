using PhoneNumbers;

namespace ActualChat.Core.NonWasm;

public static class PhoneFormatterExt
{
    public static Phone FromReadable(string s)
    {
        var phoneNumberUtil = PhoneNumberUtil.GetInstance();
        if (!phoneNumberUtil.TryParse(s, null, out var phoneNumber))
            return Phone.None;
        return phoneNumber.CreatePhone();
    }
}
