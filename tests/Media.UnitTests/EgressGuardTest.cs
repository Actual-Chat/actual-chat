using System.Net;
using ActualChat.Media.Module;
using ActualChat.Resilience;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Media.UnitTests;

public class EgressGuardTest
{
    private readonly EgressGuard _sut = NewGuard();

    [Fact]
    public async Task DoesNotFollowRedirectToPrivateAddress()
    {
        // arrange
        var handler = new RedirectHandlerMock(request => request.RequestUri!.AbsolutePath switch {
            "/start" => new (HttpStatusCode.Redirect) {
                Headers = {
                    Location = new ("http://127.0.0.1/final"),
                },
            },
            _ => new (HttpStatusCode.OK),
        });
        using var client = NewHttpClient(handler);

        // act
        var act = () => client.GetAsync("https://public.example/start");

        // assert
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public void RefusesNonHttpScheme()
    {
        // act
        var graph = OpenGraphParser.Parse("""
            <html>
            <head>
                <title>Title</title>
                <meta property="og:image" content="file:///etc/hosts">
            </head>
            </html>
            """);

        // assert
        graph.Should().NotBeNull();
        graph!.ImageUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task FollowsOrdinaryRedirect()
    {
        // arrange
        var handler = new RedirectHandlerMock(request => request.RequestUri!.AbsolutePath switch {
            "/start" => new (HttpStatusCode.Redirect) {
                Headers = {
                    Location = new ("https://public.example/final"),
                },
            },
            _ => new (HttpStatusCode.OK) {
                Content = new StringContent("ok"),
            },
        });
        using var client = NewHttpClient(handler);

        // act
        var result = await client.GetStringAsync("http://public.example/start");

        // assert
        result.Should().Be("ok");
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task ValidatesResolvedAddressBeforeConnecting()
    {
        // arrange
        var validatedAddresses = new List<IPAddress>();
        var options = new EgressHttpHandler.Options(
            _ => true,
            (_, address) => {
                validatedAddresses.Add(address);
                return false;
            });
        using var client = new HttpClient(new EgressHttpHandler(options));

        // act
        var act = () => client.GetAsync("http://localhost:1");

        // assert
        await act.Should().ThrowAsync<HttpRequestException>();
        validatedAddresses.Should().NotBeEmpty();
        validatedAddresses.Should().OnlyContain(x => IPAddress.IsLoopback(x));
    }

    [Fact]
    public void UsesConfiguredDomainDenylist()
    {
        // arrange
        var sut = NewGuard(new MediaSettings {
            CrawlingDomainDenylist = ["blocked.example"],
        });

        // act
        var result = sut.IsAllowedUri(new ("https://blocked.example/path"));

        // assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RefusesResponseOverLimit()
    {
        // arrange
        var handler = new RedirectHandlerMock(_ => new (HttpStatusCode.OK) {
            Content = new StringContent("abc"),
        });
        var guard = NewGuard();
        var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress) {
            MaxResponseContentLength = 2,
        };
        using var client = new HttpClient(new EgressHttpHandler(options, handler));

        // act
        var act = () => client.GetStringAsync("https://public.example/data");

        // assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

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

    [Fact]
    public async Task DomainDenyListBlocksMatchingHost()
    {
        // arrange
        var sut = NewGuard(new MediaSettings {
            CrawlingDomainDenylist = ["actual.chat"],
            CrawlingCidrDenylist = ["10.0.0.0/8"],
        });

        // act
        var isDeniedHostAllowed = await sut.IsAllowed("actual.chat");
        var isDeniedSubdomainAllowed = await sut.IsAllowed("cdn.actual.chat");
        var isOtherHostAllowed = await sut.IsAllowed("voxt.ai");

        // assert
        isDeniedHostAllowed.Should().BeFalse();
        isDeniedSubdomainAllowed.Should().BeFalse();
        isOtherHostAllowed.Should().BeTrue();
    }

    // Private methods

    private static EgressGuard NewGuard(MediaSettings? settings = null)
        => new (new HostInfo {
            Environment = Environments.Production,
            IsTested = true,
        },
        settings ?? new MediaSettings {
            CrawlingHostAllowList = ["public.example"],
        },
        NullLogger<EgressGuard>.Instance);

    private static HttpClient NewHttpClient(HttpMessageHandler innerHandler)
    {
        var guard = NewGuard();
        var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
        return new HttpClient(new EgressHttpHandler(options, innerHandler));
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
