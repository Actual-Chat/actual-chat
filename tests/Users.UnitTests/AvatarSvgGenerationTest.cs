using System.Xml.Linq;
using ActualChat.Users.AvatarIcons;

namespace ActualChat.Users.UnitTests;

public class AvatarSvgGenerationTest
{
    [Fact]
    public void BeamAvatarShouldGenerateConsistentSvg()
    {
        var key = "testuser123";

        var svg1 = BeamAvatars.GenerateSvg(key);
        var svg2 = BeamAvatars.GenerateSvg(key);

        svg1.Should().NotBeNullOrEmpty();
        svg2.Should().NotBeNullOrEmpty();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public void BeamAvatarShouldGenerateValidXml()
    {
        var key = "testuser456";

        var svg = BeamAvatars.GenerateSvg(key);

        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 36 36'");
    }

    [Fact]
    public void BeamAvatarShouldContainExpectedElements()
    {
        var key = "testuser789";

        var svg = BeamAvatars.GenerateSvg(key);

        svg.Should().Contain("<mask id='m'");
        svg.Should().Contain("<g mask='url(#m)'>");
        svg.Should().Contain("fill='#");  // Should have colored fills
        svg.Should().Contain("<rect");    // Should have rectangles
        svg.Should().Contain("transform='"); // Should have transforms
    }

    [Fact]
    public void BeamAvatarShouldProduceDifferentOutputForDifferentKeys()
    {
        var key1 = "user1";
        var key2 = "user2";

        var svg1 = BeamAvatars.GenerateSvg(key1);
        var svg2 = BeamAvatars.GenerateSvg(key2);

        svg1.Should().NotBe(svg2, "different keys should produce different avatars");
    }

    [Fact]
    public void BeamAvatarShouldRespectSquareParameter()
    {
        var key = "squaretest";

        var svgRound = BeamAvatars.GenerateSvg(key, square: false);
        var svgSquare = BeamAvatars.GenerateSvg(key, square: true);

        svgRound.Should().Contain("rx='72'", "round avatar should have border radius");
        svgSquare.Should().NotContain("rx='72'", "square avatar should not have border radius");
    }

    [Fact]
    public void MarbleAvatarShouldGenerateConsistentSvg()
    {
        var key = "marbleuser123";

        var svg1 = MarbleAvatars.GenerateSvg(key);
        var svg2 = MarbleAvatars.GenerateSvg(key);

        svg1.Should().NotBeNullOrEmpty();
        svg2.Should().NotBeNullOrEmpty();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public void MarbleAvatarShouldGenerateValidXml()
    {
        var key = "marbleuser456";

        var svg = MarbleAvatars.GenerateSvg(key);

        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 80 80'");
    }

    [Fact]
    public void MarbleAvatarShouldContainExpectedElements()
    {
        var key = "marbleuser789";

        var svg = MarbleAvatars.GenerateSvg(key);

        svg.Should().Contain("<mask id='m'");
        svg.Should().Contain("<path");
        svg.Should().Contain("<filter id='f'");
        svg.Should().Contain("<defs>");
        svg.Should().Contain("feGaussianBlur");  // Should have blur by default
    }

    [Fact]
    public void MarbleAvatarShouldRenderTitle()
    {
        var key = "titletest";
        var title = "A";

        var svg = MarbleAvatars.GenerateSvg(key, title: title);

        svg.Should().Contain("<text");
        svg.Should().Contain(">A</text>", "should display uppercase first letter of title");
    }

    [Fact]
    public void MarbleAvatarShouldRenderEmptyTextWhenTitleIsEmpty()
    {
        var key = "notitle";

        var svg = MarbleAvatars.GenerateSvg(key, title: "");

        svg.Should().Contain("<text");
        svg.Should().Contain("></text>", "should have empty text element when no title");
    }

    [Fact]
    public void MarbleAvatarShouldRespectDoNotBlurParameter()
    {
        var key = "blurtest";

        var svgWithBlur = MarbleAvatars.GenerateSvg(key, doNotBlur: false);
        var svgWithoutBlur = MarbleAvatars.GenerateSvg(key, doNotBlur: true);

        svgWithBlur.Should().Contain("feGaussianBlur", "should have blur effect by default");
        svgWithoutBlur.Should().NotContain("feGaussianBlur", "should not have blur when doNotBlur is true");
    }

    [Fact]
    public void MarbleAvatarShouldProduceDifferentOutputForDifferentKeys()
    {
        var key1 = "marble1";
        var key2 = "marble2";

        var svg1 = MarbleAvatars.GenerateSvg(key1);
        var svg2 = MarbleAvatars.GenerateSvg(key2);

        svg1.Should().NotBe(svg2, "different keys should produce different avatars");
    }

    [Theory]
    [InlineData("<b>bold", "&lt;")]
    [InlineData("&nbsp;", "&amp;")]
    public void MarbleAvatarShouldGenerateWellFormedSvgForSpecialTitleCharacters(string title, string encoded)
    {
        // arrange
        var key = "specialtitle";

        // act
        var svg = MarbleAvatars.GenerateSvg(key, title: title);

        // assert
        var parse = () => XDocument.Parse(svg);
        parse.Should().NotThrow("title characters must be XML-encoded before reaching the markup");
        svg.Should().Contain($">{encoded}</text>");
        svg.Should().NotContain($">{title[0]}</text>");
    }
}
