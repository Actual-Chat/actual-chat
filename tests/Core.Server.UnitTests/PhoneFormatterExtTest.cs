using PhoneNumbers;

namespace ActualChat.Core.Server.UnitTests;

public class LibPhoneNumbersExtTest
{
    [Theory]
    [InlineData("", "", "")]
    [InlineData("+1 (650) 456-78-90", "+1 (123) 456-78-90", "1-1234567890")]
    [InlineData("+1 (650) 456-78-90", "(123) 456-78-90", "1-1234567890")]
    [InlineData("+1 (650) 456-78-90", "456-78-90", "1-4567890")]
    [InlineData("+41 (123) 456-789", "+41 (123) 456-789", "41-123456789")]
    [InlineData("+41 (123) 456-789", "(123) 456-789", "41-123456789")]
    [InlineData("+41 (123) 456-789", "456-789", "41-456789")]
    // TODO(DF): wierd test case, do we really need to support it?
    // [InlineData("65((!111)123456", "65-111123456")]
    public void PhoneParserTest(string ownPhone, string source, string expected)
    {
        var phoneParser = PhoneParser.ForOwnPhone(ownPhone);
        phoneParser.Parse(source).Should().Be(new Phone(expected));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("+1 (123) 456-78-90", "1-1234567890")]
    [InlineData("+299 (123) 45-67", "299-1234567")]
    [InlineData("+41 (123) 456-789", "41-123456789")]
    [InlineData("+65 (123) 456-78", "65-12345678")]
    [InlineData("+65", "")]
    // TODO(DF): wierd test case, do we really need to support it?
    // [InlineData("65((!111)123456", "65-111123456")]
    public void ParseTest(string source, string expected)
        => PhoneExt.Parse(source, null).Should().Be(new Phone(expected));
}
