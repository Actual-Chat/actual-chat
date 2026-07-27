using System.Net;
using ActualChat.Hosting;
using ActualChat.Media.Module;
using ActualChat.Resilience;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Media.UnitTests;

public class EgressGuardTest
{
    private readonly EgressGuard _sut = new (new HostInfo {
            Environment = Environments.Production,
            IsTested = true,
        },
        new MediaSettings(),
        NullLogger<EgressGuard>.Instance);

    [Fact]
    public async Task RefusesGifRequestPastBudget()
    {
        // arrange
        var policy = new CountingRateLimitPolicy(RateLimitClass.GifProvider, 2);
        var handler = new RedirectHandlerMock(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("""{"data":{"data":[],"has_next":true}}"""),
        });
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IHttpClientFactory>(new HttpClientFactoryMock(handler))
            .AddSingleton<RateLimitPolicy>(policy)
            .AddSingleton(new MediaSettings { KlipyApiKey = "test-key" })
            .BuildServiceProvider();
        var sut = new Gifs(services);

        // act
        var first = await sut.GetTrending(1, default);
        var second = await sut.Search("hello", 1, default);
        var act = () => sut.GetTrending(2, default);

        // assert
        first.HasNext.Should().BeTrue();
        second.HasNext.Should().BeTrue();
        await act.Should().ThrowAsync<Exception>().WithMessage("*Too many requests*");
        handler.RequestCount.Should().Be(2);
    }

    [Theory]
    [InlineData("voxt.ai")]
    [InlineData("cdn.voxt.ai")]
    [InlineData("media.voxt.ai")]
    [InlineData("actual.chat")]
    [InlineData("cdn.actual.chat")]
    [InlineData("media.actual.chat")]
    public async Task ShouldAllow(string host)
    {
        var result = await _sut.IsAllowed(host);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("voxt.a1")]
    [InlineData("voxt.al")]
    [InlineData("local.voxt.ai")]
    [InlineData("local.actual.chat")]
    [InlineData("svc.cluster.local")]
    [InlineData("192.168.1.1")] // Private IP
    [InlineData("10.0.0.1")] // Private IP
    [InlineData("172.16.0.1")] // Private IP
    [InlineData("127.0.0.1")] // Localhost
    [InlineData("0.0.0.0")] // Special IP
    [InlineData("169.254.0.1")] // Link-local
    [InlineData("fc00::")] // Unique local IPv6
    [InlineData("::1")] // Localhost IPv6
    [InlineData("8.8.8.8")] // Public IP (Google DNS) - raw IPs don't produce useful previews
    [InlineData("104.16.124.96")] // Public IP (Cloudflare)
    [InlineData("2001:4860:4860::8888")] // Public IPv6 (Google DNS)
    public async Task ShouldNotAllow(string host)
    {
        var result = await _sut.IsAllowed(host);
        result.Should().BeFalse();
    }

    private sealed class RedirectHandlerMock(Func<HttpRequestMessage, HttpResponseMessage> getResponse)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(getResponse(request));
        }
    }

    private sealed class HttpClientFactoryMock(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, false);
    }

    private sealed class CountingRateLimitPolicy(RateLimitClass rateLimitClass, int limit) : RateLimitPolicy
    {
        private int _count;

        public override ValueTask Check(
            string method,
            RateLimitClass actualClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            if (actualClass != rateLimitClass || Interlocked.Increment(ref _count) <= limit)
                return default;

            throw StandardError.RateLimitExceeded(TimeSpan.FromMinutes(1));
        }
    }
}
