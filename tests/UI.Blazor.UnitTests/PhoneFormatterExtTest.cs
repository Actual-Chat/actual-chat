using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class PhoneNumberExtTest
{
    [Theory]
    [InlineData("", "")]
    [InlineData("1-1234567890", "+1 (123) 456-78-90")]
    [InlineData("299-1234567", "+299 (123) 45-67")]
    [InlineData("41-123456789", "+41 (123) 456-789")]
    [InlineData("65-12345678", "+65 (123) 456-78")]
    [InlineData("1-23", "+123")]
    public void ShouldConvertToReadableFormat(string phone, string expected)
        => new Phone(phone).ToReadable().Should().Be(expected);
}
