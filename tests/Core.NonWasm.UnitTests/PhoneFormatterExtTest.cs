namespace ActualChat.Core.NonWasm.UnitTests;

public class PhoneFormatterExtTest
{
    [Theory]
    [InlineData("", "")]
    [InlineData("+1 (123) 456-78-90", "1-1234567890")]
    [InlineData("+299 (123) 45-67", "299-1234567")]
    [InlineData("+41 (123) 456-789", "41-123456789")]
    [InlineData("+65 (123) 456-78", "65-12345678")]
    [InlineData("+65", "")]
    // TODO(DF): wierd test case, do we really need to support it?
    // [InlineData("65((!111)123456", "65-111123456")]
    public void ShouldParseFromReadableFormat(string readable, string expected)
        => PhoneFormatterExt.FromReadable(readable).Should().Be(new Phone(expected));
}
