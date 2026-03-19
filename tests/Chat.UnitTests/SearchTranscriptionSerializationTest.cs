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
        query.AssertPassesThroughAllSerializers();
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
        query.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void SearchMatch_Basic()
    {
        var match = new SearchMatch("test query", 0.95, []);
        var s = match.PassThroughAllSerializers(Out);
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
        var s = match.PassThroughAllSerializers(Out);
        s.Text.Should().Be(match.Text);
        s.Rank.Should().Be(match.Rank);
        s.Parts.Length.Should().Be(2);
    }

    [Fact]
    public void SearchMatchPart_Basic()
    {
        var part = new SearchMatchPart(new Range<int>(0, 5), 0.9);
        part.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Transcript_Basic()
    {
        var transcript = new Transcript("Hello world", LinearMap.Zero, [Languages.English]) {
            IsStable = true,
        };
        var s = transcript.PassThroughAllSerializers(Out);
        s.Text.Should().Be(transcript.Text);
        s.IsStable.Should().Be(transcript.IsStable);
        s.Languages.Length.Should().Be(1);
    }

    [Fact]
    public void Transcript_Empty()
    {
        var transcript = Transcript.New();
        var s = transcript.PassThroughAllSerializers(Out);
        s.Text.Should().Be(transcript.Text);
        s.IsStable.Should().Be(transcript.IsStable);
    }

    [Fact]
    public void TranscriptDiff_Basic()
    {
        var diff = new TranscriptDiff(new StringDiff(5, " world"), LinearMapDiff.None) {
            IsStable = false,
        };
        var s = diff.PassThroughAllSerializers(Out);
        s.TextDiff.Should().Be(diff.TextDiff);
        s.IsStable.Should().Be(diff.IsStable);
    }

    [Fact]
    public void TranscriptDiff_None()
    {
        var diff = TranscriptDiff.None;
        var s = diff.PassThroughAllSerializers(Out);
        s.TextDiff.Should().Be(diff.TextDiff);
        s.IsStable.Should().Be(diff.IsStable);
    }

    [Fact]
    public void StringDiff_Basic()
    {
        var diff = new StringDiff(5, " world");
        diff.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void StringDiff_None()
    {
        var diff = StringDiff.None;
        diff.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ContentLinkInfo_Basic()
    {
        var id = ContentId.New(ChatId.Parse("the-actual-one"));
        var info = new ContentLinkInfo(id, "Example", null, "A description");
        info.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void LocalUrl_Basic()
    {
        var url = new LocalUrl("/chat/abc");

        // MemoryPack round-trip (has [MemoryPackConstructor])
        var mpp = MemoryPackSerialized.New(url);
        Out.WriteLine($"MemoryPackSerialized: {mpp.Data.AsByteString()}");
        var s1 = MemoryPackSerialized.New<LocalUrl>(mpp.Data).Value;
        s1.Value.Should().Be(url.Value);

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
        url.AssertPassesThroughAllSerializers();
    }
}
