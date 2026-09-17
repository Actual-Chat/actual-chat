namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Fact]
    public async Task ResponseMaxAgeShouldReplaceCacheControlAndDropValidators()
    {
        // arrange
        var maxAge = TimeSpan.FromSeconds(10);
        var handler = Create(new TestSource(_ => {
            var response = Response("body");
            response.Headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromDays(30) };
            response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
            return response;
        }), maxAge);

        // act
        using var filled = await handler.Handle(Request());
        await filled!.Content.ReadAsStringAsync();
        using var hit = await handler.Handle(Request());

        // assert
        foreach (var response in new[] { filled, hit }) {
            response.Headers.CacheControl!.MaxAge.Should().Be(maxAge);
            response.Headers.ETag.Should().BeNull("a conditional re-request would bypass the cache");
            response.Content.Headers.LastModified.Should().BeNull();
        }
    }

    [Fact]
    public async Task StoredHeadersShouldSurviveWhenResponseMaxAgeIsUnset()
    {
        // arrange
        var handler = Create(new TestSource(_ => {
            var response = Response("body");
            response.Headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromDays(30) };
            return response;
        }));

        // act
        using (var filled = await handler.Handle(Request()))
            await filled!.Content.ReadAsStringAsync();
        using var hit = await handler.Handle(Request());

        // assert
        hit!.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromDays(30));
        hit.Headers.ETag!.Tag.Should().Be("\"v1\"");
    }

    private FileSystemContentHandler Create(IContentHandler source, TimeSpan? responseMaxAge)
        => new(
            new FileSystemContentHandler.Options {
                Directory = _directory,
                EncryptionKey = _key,
                ResponseMaxAge = responseMaxAge,
            },
            source);
}
