using ActualChat.Users.Phone;
using PhoneNumbers;

namespace ActualChat.Users.UnitTests.Phone;

public class VerificationCodeRoutingTest
{
    [Theory]
    [InlineData("US", "0.00830")]
    [InlineData("CA", "0.00830")]
    [InlineData("BS", "0.09040")]
    [InlineData("GB", "0.05600")]
    [InlineData("GG", "0.05240")]
    [InlineData("JE", "0.05240")]
    [InlineData("IN", "0.08320")]
    [InlineData("PW", "0.09980")]
    public void CheapDestinationsShouldPreferTwilioSms(string region, string expectedPrice)
    {
        // arrange
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber(region).ToPhone();

        // act
        var price = VerificationCodeRouting.GetPrice(phone, "twilio");
        var isSmsFirst = VerificationCodeRouting.PreferSms(phone, "twilio");

        // assert
        price.Should().Be(decimal.Parse(expectedPrice));
        isSmsFirst.Should().BeTrue();
    }

    [Theory]
    [InlineData("MX")]
    [InlineData("VN")]
    [InlineData("KG")]
    [InlineData("AM")]
    [InlineData("RU")]
    [InlineData("KZ")]
    [InlineData("IM")]
    [InlineData("JM")]
    public void ExpensiveDestinationsSharingCallingCodesShouldNotInheritCheapRates(string region)
    {
        // arrange
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber(region).ToPhone();

        // act
        var isSmsFirst = VerificationCodeRouting.PreferSms(phone, "twilio");

        // assert
        isSmsFirst.Should().BeFalse();
    }

    [Theory]
    [InlineData("US")]
    [InlineData("RU")]
    [InlineData("KZ")]
    public void SmsToShouldUseItsOwnEstimateAndPreferTelegram(string region)
    {
        // arrange
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber(region).ToPhone();

        // act
        var price = VerificationCodeRouting.GetPrice(phone, "smsto");
        var isSmsFirst = VerificationCodeRouting.PreferSms(phone, "smsto");

        // assert
        price.Should().Be(0.20m);
        isSmsFirst.Should().BeFalse();
    }

    [Theory]
    [InlineData("999-11223344", "twilio")]
    [InlineData("1-123", "twilio")]
    [InlineData("1-2015550123", "unknown")]
    public void UnknownNumberOrProviderShouldPreferTelegram(string number, string provider)
    {
        // arrange
        var phone = ActualChat.Phone.Parse(number);

        // act
        var isSmsFirst = VerificationCodeRouting.PreferSms(phone, provider);

        // assert
        isSmsFirst.Should().BeFalse();
    }

    [Fact]
    public void SnapshotShouldIncludeOnlyStrictlySubThresholdCountries()
    {
        // act
        var prices = VerificationCodeRouting.TwilioPrices;

        // assert
        VerificationCodeRouting.SmsFirstPriceThreshold.Should().Be(0.10m);
        prices.Should().HaveCount(54);
        prices.Values.Should().OnlyContain(p => p > 0 && p < VerificationCodeRouting.SmsFirstPriceThreshold);
        VerificationCodeRouting.TwilioPriceSnapshotDate.Should().Be("2026-10-08");
    }
}
