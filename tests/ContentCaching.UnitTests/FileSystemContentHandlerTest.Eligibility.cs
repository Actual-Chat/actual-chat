using System.Net;
using System.Text;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Theory]
    [InlineData("GCLB=\"a07be9fbf9bd608e\"; Max-Age=604800; Path=/; HttpOnly")]
    [InlineData("gclb=x")]
    public async Task LoadBalancerCookiesShouldNotPreventCaching(string setCookie)
    {
        // arrange
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => {
            downloadCount++;
            var response = Response("body");
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
            response.Headers.Vary.Add("Accept-Encoding");
            return response;
        }));

        // act
        using (var filled = await handler.Handle(Request()))
            await filled!.Content.ReadAsStringAsync();
        using var cached = await handler.Handle(Request());

        // assert
        (await cached!.Content.ReadAsStringAsync()).Should().Be("body");
        downloadCount.Should().Be(1);
        handler.Stats[ContentCacheOutcome.Bypass].Should().Be(0);
        cached.Headers.Contains("Set-Cookie").Should().BeFalse("a shared entry must not replay a cookie");
        var bytes = await File.ReadAllBytesAsync(GetCacheFiles().Single());
        Encoding.UTF8.GetString(bytes).Should().NotContain("GCLB");
    }

    [Theory]
    [InlineData("Session=abc")]
    [InlineData("GCLB=x, Session=abc")]
    public async Task OtherCookiesShouldStillBypassTheCache(string setCookie)
    {
        // arrange
        var handler = Create(new TestSource(_ => {
            var response = Response("body");
            foreach (var value in setCookie.Split(", "))
                response.Headers.TryAddWithoutValidation("Set-Cookie", value);
            return response;
        }));

        // act
        using var response = await handler.Handle(Request());

        // assert
        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Stats[ContentCacheOutcome.Bypass].Should().Be(1);
        GetCacheFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnexpectedVaryShouldStillBypassTheCache()
    {
        // arrange
        var handler = Create(new TestSource(_ => {
            var response = Response("body");
            response.Headers.Vary.Add("Accept");
            return response;
        }));

        // act
        using var response = await handler.Handle(Request());

        // assert
        response.Should().NotBeNull();
        handler.Stats[ContentCacheOutcome.Bypass].Should().Be(1);
        GetCacheFiles().Should().BeEmpty();
    }
}
