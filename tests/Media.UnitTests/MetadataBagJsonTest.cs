using ActualChat.Media;

namespace ActualChat.Media.UnitTests;

public sealed class MetadataBagJsonTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment TestMoment = new(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    private const string EveryMediaKeyJson =
        """
        {"Items":{"Length":74481,"FileName":"Fusion.png","ContentType":"image/png","Width":1200,
        "Height":600,"DurationMs":4200,"BeginsAt":"2026-01-01T12:00:00.0000000Z",
        "EndsAt":"2026-01-01T12:00:00.0000000Z","ContentEndsAt":"2026-01-01T12:00:00.0000000Z",
        "ClientSideBeginsAt":"2026-01-01T12:00:00.0000000Z","ReplacesMediaId":"the-old-one"}}
        """;

    private const string EveryLinkPreviewKeyJson =
        """
        {"Items":{"VideoWidth":1920,"VideoHeight":1080,
        "VideoUrl":"https://example.com/v.mp4","VideoSite":"example.com"}}
        """;

    [Fact]
    public void ReadsEveryMediaKey()
    {
        // act
        var media = new Media(MediaId.New("test"), "blob-1", 0, MediaKind.ChatEntryAttachment, MetadataBagJson.FromJson(EveryMediaKeyJson));

        // assert
        media.Length.Should().Be(74481L);
        media.FileName.Should().Be("Fusion.png");
        media.ContentType.Should().Be("image/png");
        media.Width.Should().Be(1200);
        media.Height.Should().Be(600);
        media.DurationMs.Should().Be(4200L);
        media.BeginsAt.Should().Be(TestMoment);
        media.EndsAt.Should().Be(TestMoment);
        media.ContentEndsAt.Should().Be(TestMoment);
        media.ClientSideBeginsAt.Should().Be(TestMoment);
    }

    [Fact]
    public void ReadsEveryLinkPreviewKey()
    {
        // act
        var linkPreview = new LinkPreview { Id = "lp-1", Metadata = MetadataBagJson.FromJson(EveryLinkPreviewKeyJson) };

        // assert
        linkPreview.VideoWidth.Should().Be(1920);
        linkPreview.VideoHeight.Should().Be(1080);
        linkPreview.VideoUrl.Should().Be("https://example.com/v.mp4");
        linkPreview.VideoSite.Should().Be("example.com");
    }

    [Fact]
    public void WritesTheShapeItReads()
    {
        // arrange
        var bag = MetadataBagJson.FromJson(EveryMediaKeyJson);

        // act
        var json = bag.ToJson();

        // assert
        json.Should().StartWith("""{"Items":{""");
        json.Should().NotContain("$type").And.NotContain("RawItems");
        MetadataBagJson.FromJson(json).ToJson().Should().Be(json);
    }

    [Fact]
    public void RoundTripsValuesSetThroughTypedAccessors()
    {
        // arrange
        var media = new Media(MediaId.New("test"), "blob-1", 0, MediaKind.ChatEntryVideo, MetadataBag.Empty) {
            Length = 1024L,
            FileName = "clip.mp4",
            ContentType = "video/mp4",
            Width = 640,
            Height = 480,
            DurationMs = 9000L,
            BeginsAt = TestMoment,
        };

        // act
        var result = media with { Metadata = MetadataBagJson.FromJson(media.Metadata.ToJson()) };

        // assert
        result.Length.Should().Be(1024L);
        result.FileName.Should().Be("clip.mp4");
        result.ContentType.Should().Be("video/mp4");
        result.Width.Should().Be(640);
        result.Height.Should().Be(480);
        result.DurationMs.Should().Be(9000L);
        result.BeginsAt.Should().Be(TestMoment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"Items":null}""")]
    public void ReadsUnusableJsonAsEmpty(string? json)
    {
        // act
        var act = () => MetadataBagJson.FromJson(json);

        // assert
        act.Should().NotThrow();
    }
}
