namespace ActualChat.Chat.UnitTests;

// Guards ChatNews.ToSlim - the projection served by IChats.GetNews: it must keep
// everything chat list items render and drop the payload heavyweights.
public sealed class ChatNewsToSlimTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ToSlimKeepsWhatChatListRendersAndDropsTheRest()
    {
        // arrange
        var news = ChatNewsTestData.CreateChatNews();
        var entry = news.LastTextEntry!;

        // act
        var slim = news.ToSlim();
        var slimEntry = slim.LastTextEntry!;

        // assert
        slim.TextEntryLidRange.Should().Be(news.TextEntryLidRange);
        slimEntry.Should().BeOfType<TextEntry>();
        slimEntry.Id.Should().Be(entry.Id);
        slimEntry.Version.Should().Be(entry.Version);
        slimEntry.Flags.Should().Be(entry.Flags);
        slimEntry.AuthorId.Should().Be(entry.AuthorId);
        slimEntry.BeginsAt.Should().Be(entry.BeginsAt);
        slimEntry.EndsAt.Should().Be(entry.EndsAt);
        slimEntry.Content.Should().Be(entry.Content);
        slimEntry.RepliedEntryLid.Should().Be(entry.RepliedEntryLid);
        slimEntry.HasAudio.Should().BeTrue();

        slimEntry.Forwarded.Should().BeNull();
        slimEntry.LinkPreviewIds.Should().BeEmpty();
        slimEntry.LinkPreviews.Should().BeEmpty();
        slimEntry.ClientId.Should().BeEmpty();
        slimEntry.Audio!.TimeMap.IsEmpty.Should().BeTrue();

        var attachment = slimEntry.Attachments.Should().ContainSingle().Subject;
        attachment.Media.ContentType.Should().Be("image/jpeg");
        attachment.Media.FileName.Should().Be("photo.jpg");
        attachment.Media.Width.Should().Be(0);
        attachment.Media.Length.Should().Be(0);
        attachment.ThumbnailMedia.Should().BeNull();
    }

    [Fact]
    public void ToSlimShrinksSerializedPayload()
    {
        // arrange
        var news = ChatNewsTestData.CreateChatNews();
        var s = MessagePackByteSerializer.Default;

        // act
        using var fullBuffer = s.Write(news, typeof(ChatNews));
        var fullLength = fullBuffer.WrittenSpan.Length;
        using var slimBuffer = s.Write(news.ToSlim(), typeof(ChatNews));
        var slimLength = slimBuffer.WrittenSpan.Length;

        // assert
        Out.WriteLine($"Full: {fullLength} bytes, slim: {slimLength} bytes");
        slimLength.Should().BeLessThan(fullLength / 2);
    }

    [Fact]
    public void ToSlimWithoutLastTextEntryIsNoOp()
    {
        // arrange
        var news = new ChatNews(new Range<long>(1, 124));

        // act
        var slim = news.ToSlim();

        // assert
        slim.Should().BeSameAs(news);
    }
}
