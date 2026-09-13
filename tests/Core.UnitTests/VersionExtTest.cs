namespace ActualChat.Core.UnitTests;

public class VersionExtTest
{
    [Theory]
    [InlineData("2.17.246+2b0e2c1a3f", "2.17.246")]
    [InlineData("2.19.6-alpha+2b0e2c1a3f", "2.19.6")]
    [InlineData("2.17", "2.17.0")]
    [InlineData("2.17.246.0", "2.17.246")]
    [InlineData("2.17.0.0", "2.17.0")]
    [InlineData("v2.17.246 2b0e2c1a3f", "2.17.246")]
    [InlineData("  2.17.246  ", "2.17.246")]
    [InlineData("3", "3.0.0")]
    public void TryParseBuildVersionShouldNormalizeToThreeComponents(string input, string expected)
    {
        // act
        var isParsed = VersionExt.TryParseBuildVersion(input, out var version);

        // assert
        isParsed.Should().BeTrue();
        version!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("nonsense")]
    [InlineData("2.x.3")]
    [InlineData("2..3")]
    [InlineData("1.2.3.4.5")]
    public void TryParseBuildVersionShouldRejectNonVersions(string? input)
    {
        // act
        var isParsed = VersionExt.TryParseBuildVersion(input, out var version);

        // assert
        isParsed.Should().BeFalse();
        version.Should().BeNull();
    }

    [Fact]
    public void ParseBuildVersionShouldFallBackToZero()
    {
        // act
        var parsed = VersionExt.ParseBuildVersion("nonsense");

        // assert
        parsed.Should().Be(VersionExt.Zero);
    }

    [Fact]
    public void ParsedVersionsShouldCompareByBuildNumber()
    {
        // arrange
        var older = VersionExt.ParseBuildVersion("2.17.246+abc");
        var newer = VersionExt.ParseBuildVersion("2.17.247");
        var nextTrain = VersionExt.ParseBuildVersion("2.18.1-alpha+abc");

        // assert
        newer.Should().BeGreaterThan(older);
        nextTrain.Should().BeGreaterThan(newer);
        VersionExt.ParseBuildVersion("2.17.246.0").Should().Be(older);
    }
}
