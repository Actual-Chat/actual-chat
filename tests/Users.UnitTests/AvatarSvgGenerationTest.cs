using ActualChat.Users;
using ActualChat.Users.AvatarIcons;

namespace ActualChat.Testing.Users;

public class AvatarSvgGenerationTest
{
    [Fact]
    public void BeamAvatar_GeneratesSvg_WithConsistentOutput()
    {
        // Arrange
        var key = "testuser123";

        // Act
        var svg1 = BeamAvatars.GenerateSvg(key);
        var svg2 = BeamAvatars.GenerateSvg(key);

        // Assert
        svg1.Should().NotBeNullOrEmpty();
        svg2.Should().NotBeNullOrEmpty();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public void BeamAvatar_GeneratesSvg_WithValidXml()
    {
        // Arrange
        var key = "testuser456";

        // Act
        var svg = BeamAvatars.GenerateSvg(key);

        // Assert
        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 36 36'");
    }

    [Fact]
    public void BeamAvatar_GeneratesSvg_ContainsExpectedElements()
    {
        // Arrange
        var key = "testuser789";

        // Act
        var svg = BeamAvatars.GenerateSvg(key);

        // Assert  - should contain mask, background, wrapper, and face elements
        svg.Should().Contain("<mask id='m'");
        svg.Should().Contain("<g mask='url(#m)'>");
        svg.Should().Contain("fill='#");  // Should have colored fills
        svg.Should().Contain("<rect");    // Should have rectangles
        svg.Should().Contain("transform='"); // Should have transforms
    }

    [Fact]
    public void BeamAvatar_GeneratesSvg_DifferentKeysProduceDifferentOutput()
    {
        // Arrange
        var key1 = "user1";
        var key2 = "user2";

        // Act
        var svg1 = BeamAvatars.GenerateSvg(key1);
        var svg2 = BeamAvatars.GenerateSvg(key2);

        // Assert
        svg1.Should().NotBe(svg2, "different keys should produce different avatars");
    }

    [Fact]
    public void BeamAvatar_GeneratesSvg_SquareParameter()
    {
        // Arrange
        var key = "squaretest";

        // Act
        var svgRound = BeamAvatars.GenerateSvg(key, square: false);
        var svgSquare = BeamAvatars.GenerateSvg(key, square: true);

        // Assert
        svgRound.Should().Contain("rx='72'", "round avatar should have border radius");
        svgSquare.Should().NotContain("rx='72'", "square avatar should not have border radius");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_WithConsistentOutput()
    {
        // Arrange
        var key = "marbleuser123";

        // Act
        var svg1 = MarbleAvatars.GenerateSvg(key);
        var svg2 = MarbleAvatars.GenerateSvg(key);

        // Assert
        svg1.Should().NotBeNullOrEmpty();
        svg2.Should().NotBeNullOrEmpty();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_WithValidXml()
    {
        // Arrange
        var key = "marbleuser456";

        // Act
        var svg = MarbleAvatars.GenerateSvg(key);

        // Assert
        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 80 80'");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_ContainsExpectedElements()
    {
        // Arrange
        var key = "marbleuser789";

        // Act
        var svg = MarbleAvatars.GenerateSvg(key);

        // Assert - should contain mask, paths, filter, and defs
        svg.Should().Contain("<mask id='m'");
        svg.Should().Contain("<path");
        svg.Should().Contain("<filter id='f'");
        svg.Should().Contain("<defs>");
        svg.Should().Contain("feGaussianBlur");  // Should have blur by default
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_WithTitle()
    {
        // Arrange
        var key = "titletest";
        var title = "A";

        // Act
        var svg = MarbleAvatars.GenerateSvg(key, title: title);

        // Assert
        svg.Should().Contain("<text");
        svg.Should().Contain(">A</text>", "should display uppercase first letter of title");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_WithoutTitleWhenEmpty()
    {
        // Arrange
        var key = "notitle";

        // Act
        var svg = MarbleAvatars.GenerateSvg(key, title: "");

        // Assert
        svg.Should().Contain("<text");
        svg.Should().Contain("></text>", "should have empty text element when no title");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_DoNotBlurParameter()
    {
        // Arrange
        var key = "blurtest";

        // Act
        var svgWithBlur = MarbleAvatars.GenerateSvg(key, doNotBlur: false);
        var svgWithoutBlur = MarbleAvatars.GenerateSvg(key, doNotBlur: true);

        // Assert
        svgWithBlur.Should().Contain("feGaussianBlur", "should have blur effect by default");
        svgWithoutBlur.Should().NotContain("feGaussianBlur", "should not have blur when doNotBlur is true");
    }

    [Fact]
    public void MarbleAvatar_GeneratesSvg_DifferentKeysProduceDifferentOutput()
    {
        // Arrange
        var key1 = "marble1";
        var key2 = "marble2";

        // Act
        var svg1 = MarbleAvatars.GenerateSvg(key1);
        var svg2 = MarbleAvatars.GenerateSvg(key2);

        // Assert
        svg1.Should().NotBe(svg2, "different keys should produce different avatars");
    }
}
