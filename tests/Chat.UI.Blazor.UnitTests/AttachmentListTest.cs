using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AttachmentListTest
{
    [Fact]
    public void NewListShouldStartUncommitted()
    {
        // act
        var list = new AttachmentList();

        // assert
        list.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public void CommitShouldBeIdempotent()
    {
        // arrange
        var list = new AttachmentList();

        // act
        list.Commit();
        list.Commit();

        // assert
        list.IsCommitted.Should().BeTrue();
    }

    [Fact]
    public void HasReEncodableImagesShouldBeFalseForAGifOnlyDraft()
    {
        // arrange
        var list = new AttachmentList();
        var gif = new Attachment("clip.gif", "image/gif", 1000, new Size2D(10, 10));

        // act
        list.Add(gif);

        // assert
        list.HasReEncodableImages.Should().BeFalse();
    }

    [Fact]
    public void HasReEncodableImagesShouldBeTrueWhenAJpegIsMixedInWithAGif()
    {
        // arrange
        var list = new AttachmentList();
        var gif = new Attachment("clip.gif", "image/gif", 1000, new Size2D(10, 10));
        var jpeg = new Attachment("photo.jpg", "image/jpeg", 2000, new Size2D(20, 20));

        // act
        list.Add(gif);
        list.Add(jpeg);

        // assert
        list.HasReEncodableImages.Should().BeTrue();
    }
}
