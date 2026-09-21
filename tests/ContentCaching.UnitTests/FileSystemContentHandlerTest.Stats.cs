using System.Net;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Fact]
    public async Task StatsShouldCountFillsHitsAndBypasses()
    {
        // arrange
        const string body = "cached body";
        var handler = Create(new TestSource(_ => Response(body)));
        var request = Request();

        // act
        using (var filled = await handler.Handle(request))
            await filled!.Content.ReadAsStringAsync();
        using (var hit = await handler.Handle(request))
            await hit!.Content.ReadAsStringAsync();
        using var bypassed = await handler.Handle(request with { Method = HttpMethod.Head });

        // assert
        var stats = handler.Stats;
        stats[ContentCacheOutcome.StartedFill].Should().Be(1);
        stats[ContentCacheOutcome.Hit].Should().Be(1);
        stats[ContentCacheOutcome.Bypass].Should().Be(1);
        stats[ContentCacheOutcome.Error].Should().Be(0);
        stats.FetchedByteCount.Should().Be(body.Length, "the body is downloaded once");
        stats.ServedByteCount.Should().Be(2 * body.Length, "both readers are served through the cache");
    }

    [Fact]
    public async Task StatsShouldCountAnUnusableEntryAsAnError()
    {
        // arrange
        var handler = Create(new TestSource(_ => Response("body")));
        var request = Request();
        using (var filled = await handler.Handle(request))
            await filled!.Content.ReadAsStringAsync();
        var path = GetCacheFiles().Single();

        // act
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);
        using var refilled = await handler.Handle(request);

        // assert
        (await refilled!.Content.ReadAsStringAsync()).Should().Be("body");
        handler.Stats[ContentCacheOutcome.Error].Should().Be(1);
        handler.Stats[ContentCacheOutcome.Hit].Should().Be(0);
        handler.Stats[ContentCacheOutcome.StartedFill].Should().Be(2);
    }

    [Fact]
    public async Task StatsShouldCountReadersThatJoinAnActiveFill()
    {
        // arrange - the source parks after its head, so the fill is still active when the second
        // reader arrives; a body that downloads at once is published before it, and that's a hit
        using var source = new GatedContentStream();
        var content = new StreamContent(source);
        content.Headers.ContentLength = 8;
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var handler = Create(new TestSource(_ => upstream));
        var request = Request();

        // act
        using var first = await handler.Handle(request);
        using var second = await handler.Handle(request);
        source.Release();

        // assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        handler.Stats[ContentCacheOutcome.StartedFill].Should().Be(1);
        handler.Stats[ContentCacheOutcome.JoinedFill].Should().Be(1);
    }
}
