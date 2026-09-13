using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AttachmentTest
{
    [Theory]
    [InlineData("image/gif", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/heic", true)]
    public void ShouldKnowWhatCanBeReEncoded(string fileType, bool expected)
    {
        // act
        var attachment = new Attachment("x", fileType, 1000, new Size2D(10, 10));

        // assert
        attachment.IsReEncodable.Should().Be(expected);
    }

    [Theory]
    [InlineData("image/gif", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("image/jpeg", true)]
    public void ShouldReachTheProcessorEvenWhenItCannotBeReEncoded(string fileType, bool expected)
    {
        // act
        var attachment = new Attachment("x", fileType, 1000, new Size2D(10, 10));

        // assert
        attachment.IsProcessableImage.Should().Be(expected);
    }

    [Fact]
    public void GifShouldCarryAPlaceholderWithoutBeingReEncodable()
    {
        // arrange
        var attachment = new Attachment("clip.gif", "image/gif", 1000, new Size2D(10, 10));

        // act
        attachment = attachment with { Placeholder = "cGxhY2Vob2xkZXI=" };

        // assert
        attachment.IsProcessableImage.Should().BeTrue();
        attachment.IsReEncodable.Should().BeFalse();
        attachment.Placeholder.Should().NotBeEmpty();
    }
}
