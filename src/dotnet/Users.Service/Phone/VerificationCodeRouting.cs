using System.Collections.Frozen;
using PhoneNumbers;

namespace ActualChat.Users.Phone;

public static class VerificationCodeRouting
{
    public const decimal SmsFirstPriceThreshold = 0.10m;
    public const decimal SmsToEstimatedPrice = 0.20m;
    public const string TwilioPriceSnapshotDate = "2026-10-08";

    // Maximum outbound carrier rate per segment, USD, from Twilio's 2026-10-08 public pricing CSV:
    // https://assets.cdn.prod.twilio.com/pricing-csv/SMSPricing.csv. Only rates below the threshold are included.
    public static readonly FrozenDictionary<string, decimal> TwilioPrices = new Dictionary<string, decimal> {
        ["AT"] = 0.09790m,
        ["AU"] = 0.05150m,
        ["BH"] = 0.03860m,
        ["BN"] = 0.06500m,
        ["BR"] = 0.05990m,
        ["BS"] = 0.09040m,
        ["CA"] = 0.00830m,
        ["CH"] = 0.07690m,
        ["CL"] = 0.07970m,
        ["CO"] = 0.05920m,
        ["CY"] = 0.08640m,
        ["CZ"] = 0.07060m,
        ["DK"] = 0.05920m,
        ["EE"] = 0.09580m,
        ["ES"] = 0.08750m,
        ["FI"] = 0.08610m,
        ["FM"] = 0.07350m,
        ["FO"] = 0.07170m,
        ["FR"] = 0.07980m,
        ["GB"] = 0.05600m,
        ["GG"] = 0.05240m,
        ["GL"] = 0.04100m,
        ["GR"] = 0.06570m,
        ["GU"] = 0.08470m,
        ["HK"] = 0.07230m,
        ["HU"] = 0.09100m,
        ["IE"] = 0.07790m,
        ["IN"] = 0.08320m,
        ["IS"] = 0.07190m,
        ["IT"] = 0.09270m,
        ["JE"] = 0.05240m,
        ["JP"] = 0.08900m,
        ["KR"] = 0.05240m,
        ["LI"] = 0.03620m,
        ["LT"] = 0.05780m,
        ["LU"] = 0.08180m,
        ["LV"] = 0.08010m,
        ["MO"] = 0.05900m,
        ["MT"] = 0.06890m,
        ["NF"] = 0.02817m,
        ["NO"] = 0.06970m,
        ["PL"] = 0.04570m,
        ["PR"] = 0.05400m,
        ["PT"] = 0.05010m,
        ["PW"] = 0.09980m,
        ["RO"] = 0.07810m,
        ["SE"] = 0.06460m,
        ["SG"] = 0.05910m,
        ["SK"] = 0.08830m,
        ["TH"] = 0.03050m,
        ["TR"] = 0.03050m,
        ["TW"] = 0.08420m,
        ["US"] = 0.00830m,
        ["UY"] = 0.08520m,
    }.ToFrozenDictionary();

    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    public static bool PreferSms(ActualChat.Phone phone, string provider)
    {
        var price = GetPrice(phone, provider);

        return price is < SmsFirstPriceThreshold;
    }

    public static decimal? GetPrice(ActualChat.Phone phone, string provider)
    {
        if (provider == "smsto")
            return SmsToEstimatedPrice;
        if (provider != "twilio")
            return null;
        if (!PhoneFormatterExt.TryParse(PhoneUtil, phone.E164Value, null, out var number)
            || number is null || !PhoneUtil.IsValidNumber(number))
            return null;

        var region = PhoneUtil.GetRegionCodeForNumber(number);

        return region is not null && TwilioPrices.TryGetValue(region, out var price) ? price : null;
    }
}
