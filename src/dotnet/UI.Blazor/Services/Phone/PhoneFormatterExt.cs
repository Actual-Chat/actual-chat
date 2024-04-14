namespace ActualChat.UI.Blazor.Services;

public static class PhoneFormatterExt
{
    public static string ToReadable(this Phone phone)
    {
        if (phone.IsNone)
            return "";

        var phoneCode = PhoneCodes.GetByCode(phone.Code);
        if (phoneCode is null)
            return $"+{phone.Code} {phone.Number}";

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        const int areaCodeLength = 3;
        const int defaultGroupSize = 3;
        sb.Append(phoneCode.DisplayCode);
        if (phone.Number.Length < areaCodeLength)
            return sb.Append(phone.Number).ToStringAndRelease();

        sb.Append(" (")
            .Append(phone.Number.AsSpan(0, areaCodeLength))
            .Append(") ");

        var tailLength = ((phone.Number.Length - areaCodeLength) % defaultGroupSize) switch {
            1 => 4, // tail: ...-11-11
            2 => 2, // tail: -11
            _ => 0, // no tail, all groups by three
        };

        Append(areaCodeLength, defaultGroupSize, tailLength);
        Append(phone.Number.Length - tailLength, 2, 0);
        return sb.ToStringAndRelease();

        void Append(int startIdx, int groupSize, int skipTailLength)
        {
            for (int i = startIdx; i < phone.Number.Length - skipTailLength; i += groupSize) {
                // don't append '-' for first group
                if (i != areaCodeLength)
                    sb.Append('-');
                sb.Append(phone.Number.AsSpan(i, groupSize));
            }
        }
    }
}
