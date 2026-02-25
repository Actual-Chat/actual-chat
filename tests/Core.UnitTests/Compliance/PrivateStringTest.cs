namespace ActualChat.Core.UnitTests.Compliance;

public class PrivateStringTest
{
    [Fact]
    public void PrivateToStringPassesThroughWhenInactive()
    {
        var p = "Hello world".ToPrivate();
        p.ToString().Should().Be("Hello world");
    }

    [Fact]
    public void PrivateToStringMasksWhenActive()
    {
        var p = "Hello world".ToPrivate();
        using var _ = Sanitizer.Activate();
        p.ToString().Should().Be("<<He* [8-15]>>");
    }

    [Fact]
    public void PrivateNullSourceIsHandled()
    {
        var p = default(PrivateString);
        p.Source.Should().Be("");
        p.ToString().Should().Be("");

        using var _ = Sanitizer.Activate();
        p.ToString().Should().Be("");
    }

    [Fact]
    public void PrivateEqualityWorks()
    {
        var a = "test".ToPrivate();
        var b = "test".ToPrivate();
        var c = "other".ToPrivate();

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
    }

    [Fact]
    public void PrivateImplementsISensitive()
    {
        var p = "test".ToPrivate();
#pragma warning disable CS0183
        (p is ISanitized).Should().BeTrue();
#pragma warning restore CS0183
    }

    [Fact]
    public void PrivateWorksInStringInterpolation()
    {
        var p = "Secret".ToPrivate();
        $"Content: {p}".Should().Be("Content: Secret");

        using var _ = Sanitizer.Activate();
        $"Content: {p}".Should().Be("Content: <<Se* [4-7]>>");
    }
}
