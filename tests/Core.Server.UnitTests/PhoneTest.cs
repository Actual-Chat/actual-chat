namespace ActualChat.Core.Server.UnitTests;

public class PhoneTest
{
    [Theory]
    [InlineData("1-1234567890", true, "+1 (123) 456-78-90")]
    [InlineData("1-1234567890", false, "+1(123)456-78-90")]
    [InlineData("299-1234567", true, "+299 (123) 45-67")]
    [InlineData("41-123456789", true, "+41 (123) 456-789")]
    [InlineData("65-12345678", true, "+65 (123) 456-78")]
    [InlineData("1-23", true, "+123")]
    public void ShouldConvertToReadableFormat(string phone, bool withSpaces, string expected)
        => Phone.Parse(phone).ToReadable(withSpaces).Should().Be(expected);
}
