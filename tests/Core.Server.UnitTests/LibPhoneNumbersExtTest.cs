using PhoneNumbers;

namespace ActualChat.Core.Server.UnitTests;

public class LibPhoneNumbersExtTest
{
    [Theory]
    [InlineData("", "", "")]
    [InlineData("+1 (650) 456-78-90", "+1 (123) 456-78-90", "1-1234567890")]
    [InlineData("+1 (650) 456-78-90", "(123) 456-78-90", "1-1234567890")]
    [InlineData("+1 (650) 456-78-90", "456-78-90", "1-4567890")]
    [InlineData("+1 (650) 456-78-90", "*111#", "")]
    [InlineData("+41 (123) 456-789", "+41 (123) 456-789", "41-123456789")]
    [InlineData("+41 (123) 456-789", "(123) 456-789", "41-123456789")]
    [InlineData("+41 (123) 456-789", "456-789", "41-456789")]
    // TODO(DF): wierd test case, do we really need to support it?
    // [InlineData("65((!111)123456", "65-111123456")]
    public void PhoneParserTest(string ownPhone, string source, string sExpected)
    {
        var expected = sExpected.IsNullOrEmpty() ? null : Phone.Parse(sExpected);
        var phoneParser = PhoneParser.ForOwnPhone(ownPhone);
        phoneParser.ParseNullable(source).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("+1 (123) 456-78-90", "1-1234567890")]
    [InlineData("+299 (123) 45-67", "299-1234567")]
    [InlineData("+41 (123) 456-789", "41-123456789")]
    [InlineData("+65 (123) 456-78", "65-12345678")]
    [InlineData("+65", "")]
    [InlineData("*111#", "")]
    // TODO(DF): wierd test case, do we really need to support it?
    // [InlineData("65((!111)123456", "65-111123456")]
    public void ParseTest(string source, string expected)
        => PhoneExt.ParseNullable(source, null).Should().Be(expected.IsNullOrEmpty() ? null: Phone.Parse(expected));

    [Theory]
    [InlineData(1, 1)]       // US
    [InlineData(44, 44)]     // GB
    [InlineData(44, 44, 1)]  // GB w/ default prefix
    [InlineData(7, 7)]       // RU
    [InlineData(49, 49)]     // DE
    [InlineData(0, 0)]       // Invalid
    [InlineData(-1, 0)]      // Negative
    [InlineData(9999, 0)]    // Non-existent
    [InlineData(9999, 1, 1)] // Non-existent w/ default prefix
    public void GetExampleCountryPhoneTest(int prefix, int expectedPrefix, int defaultPrefix = 0)
    {
        var phone = PhoneExt.GetExampleCountryPhone(prefix, defaultPrefix);
        if (expectedPrefix == 0) {
            phone.Should().BeNull();
            return;
        }

        var parsedPrefix = int.Parse(phone!.Code);
        parsedPrefix.Should().Be(expectedPrefix);
        phone.Number.Should().NotBeNullOrEmpty();
        // Verify the result is parsable
        PhoneExt.TryParse($"+{phone}", null, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]     // US → +1(201)555-0123 or similar
    [InlineData(44)]    // GB
    [InlineData(49)]    // DE
    public void GetExampleCountryPhoneWithToReadableTest(int prefix)
    {
        var phone = PhoneExt.GetExampleCountryPhone(prefix);
        phone.Should().NotBeNull();
        var readable = phone.ToReadable(withSpaces: false);
        readable.Should().StartWith("+");
        readable.Should().Contain("(");
        // Must be parsable back
        PhoneExt.TryParse(readable, null, out _).Should().BeTrue();
    }
}
