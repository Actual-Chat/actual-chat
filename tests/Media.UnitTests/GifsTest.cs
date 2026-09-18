using System.Net;
using ActualChat.Media.Module;
using ActualChat.Resilience;

namespace ActualChat.Media.UnitTests;

public class GifsTest
{
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
            .AddSingleton(c => new RateLimitIdentityResolver(c))
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

    // Nested types

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
