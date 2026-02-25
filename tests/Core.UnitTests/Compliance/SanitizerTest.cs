namespace ActualChat.Core.UnitTests.Compliance;

public class SanitizerTest
{
    [Fact]
    public void IsNotActiveByDefault()
        => Sanitizer.IsActive.Should().BeFalse();

    [Fact]
    public void ActivateSetsAndRestoresIsActive()
    {
        Sanitizer.IsActive.Should().BeFalse();
        using (Sanitizer.Activate()) {
            Sanitizer.IsActive.Should().BeTrue();
        }
        Sanitizer.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NestedActivateRestoresCorrectly()
    {
        Sanitizer.IsActive.Should().BeFalse();
        using (Sanitizer.Activate()) {
            Sanitizer.IsActive.Should().BeTrue();
            using (Sanitizer.Activate()) {
                Sanitizer.IsActive.Should().BeTrue();
            }
            Sanitizer.IsActive.Should().BeTrue();
        }
        Sanitizer.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("A", "<<* [1]>>")]
    [InlineData("Hi", "<<* [2-3]>>")]
    [InlineData("Hey", "<<* [2-3]>>")]
    [InlineData("Test", "<<Te* [4-7]>>")]
    [InlineData("Hello!!", "<<He* [4-7]>>")]
    [InlineData("12345678", "<<12* [8-15]>>")]
    [InlineData("Hello world", "<<He* [8-15]>>")]
    [InlineData("Hello world12345", "<<He* [16-31]>>")]
    public void MaskPrivateWhenActive(string? input, string? expected)
    {
        using var _ = Sanitizer.Activate();
        Sanitizer.MaskPrivate(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Hello world", "Hello world")]
    public void MaskPrivatePassesThroughWhenInactive(string? input, string? expected)
        => Sanitizer.MaskPrivate(input).Should().Be(expected);
}
