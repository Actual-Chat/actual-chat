using ActualChat.Search;
using ActualChat.Transcription;

namespace ActualChat.Chat.UnitTests;

public class SearchTranscriptionSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EntrySearchQuery_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var query = new EntrySearchQuery {
            Criteria = "hello world",
            ChatId = chatId,
            Skip = 0,
            Limit = 20,
        };
        query.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContactSearchQuery_Basic()
    {
        var query = new ContactSearchQuery {
            Scope = SearchScope.People,
            Criteria = "john",
            Own = true,
            Limit = 20,
        };
        query.AssertPassesThroughSerializers();
    }

    [Fact]
    public void SearchMatch_Basic()
    {
        var match = new SearchMatch("test query", 0.95, []);
        var s = match.PassThroughSerializers(Out);
        s.Text.Should().Be(match.Text);
        s.Rank.Should().Be(match.Rank);
        s.Parts.Should().BeEmpty();
    }

    [Fact]
    public void SearchMatch_WithParts()
    {
        var parts = new[] {
            new SearchMatchPart(new Range<int>(0, 4), 0.9),
            new SearchMatchPart(new Range<int>(5, 10), 0.8),
        };
        var match = new SearchMatch("test query", 0.95, parts);
        var s = match.PassThroughSerializers(Out);
        s.Text.Should().Be(match.Text);
        s.Rank.Should().Be(match.Rank);
        s.Parts.Length.Should().Be(2);
    }

    [Fact]
    public void SearchMatchPart_Basic()
    {
        var part = new SearchMatchPart(new Range<int>(0, 5), 0.9);
        part.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Transcript_Basic()
    {
        var transcript = new Transcript("Hello world", LinearMap.Zero, [Languages.English]) {
            IsStable = true,
        };
        var s = transcript.PassThroughSerializers(Out);
        s.Text.Should().Be(transcript.Text);
        s.IsStable.Should().Be(transcript.IsStable);
        s.Languages.Length.Should().Be(1);
    }

    [Fact]
    public void Transcript_Empty()
    {
        var transcript = Transcript.New();
        var s = transcript.PassThroughSerializers(Out);
        s.Text.Should().Be(transcript.Text);
        s.IsStable.Should().Be(transcript.IsStable);
    }

    [Fact]
    public void TranscriptDiff_Basic()
    {
        var diff = new TranscriptDiff(new StringDiff(5, " world"), LinearMapDiff.None) {
            IsStable = false,
        };
        var s = diff.PassThroughSerializers(Out);
        s.TextDiff.Should().Be(diff.TextDiff);
        s.IsStable.Should().Be(diff.IsStable);
    }

    [Fact]
    public void TranscriptDiff_None()
    {
        var diff = TranscriptDiff.None;
        var s = diff.PassThroughSerializers(Out);
        s.TextDiff.Should().Be(diff.TextDiff);
        s.IsStable.Should().Be(diff.IsStable);
    }

    [Fact]
    public void StringDiff_Basic()
    {
        var diff = new StringDiff(5, " world");
        diff.AssertPassesThroughSerializers();
    }

    [Fact]
    public void StringDiff_None()
    {
        var diff = StringDiff.None;
        diff.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ContentLinkInfo_Basic()
    {
        var id = ContentId.New(ChatId.Parse("the-actual-one"));
        var info = new ContentLinkInfo(id, "Example", null, "A description");
        info.AssertPassesThroughSerializers();
    }

    [Fact]
    public void LocalUrl_Basic()
    {
        var url = new LocalUrl("/chat/abc");

        // MessagePack round-trip
        var mp = MessagePackSerialized.New(url);
        Out.WriteLine($"MessagePackSerialized: {mp.Data.AsByteString()}");
        var s2 = MessagePackSerialized.New<LocalUrl>(mp.Data).Value;
        s2.Value.Should().Be(url.Value);
    }

    [Fact]
    public void LocalUrl_Root()
    {
        var url = new LocalUrl("/");
        url.AssertPassesThroughSerializers();
    }
}
