namespace ActualChat.Core.UnitTests.Compliance;

public class SensitiveTest
{
    [Fact]
    public void ToStringPassesThroughWhenInactive()
    {
        var s = new Sensitive("Hello world");
        s.ToString().Should().Be("Hello world");
    }

    [Fact]
    public void ToStringMasksWhenActive()
    {
        var s = new Sensitive("Hello world");
        using var _ = Sanitizer.Activate();
        s.ToString().Should().Be("<<He* [8-15]>>");
    }

    [Fact]
    public void NullSourceIsHandled()
    {
        var s = new Sensitive(null);
        s.ToString().Should().Be("");

        using var _ = Sanitizer.Activate();
        s.ToString().Should().Be("");
    }

    [Fact]
    public void ImplementsISensitive()
    {
        var s = new Sensitive("test");
#pragma warning disable CS0183 // 'is' expression's given expression is always of the provided type
        (s is ISensitive).Should().BeTrue();
#pragma warning restore CS0183 // 'is' expression's given expression is always of the provided type
    }

    [Fact]
    public void WorksInStringInterpolation()
    {
        var s = new Sensitive("Hello world");
        $"Content: {s}".Should().Be("Content: Hello world");

        using var _ = Sanitizer.Activate();
        $"Content: {s}".Should().Be("Content: <<He* [8-15]>>");
    }

    [Fact]
    public void EqualityWorks()
    {
        var a = new Sensitive("test");
        var b = new Sensitive("test");
        var c = new Sensitive("other");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
    }

    // Sensitive.Private / ToPrivate() tests

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
        var p = new Sensitive.Private(null);
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
        (p is ISensitive).Should().BeTrue();
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
