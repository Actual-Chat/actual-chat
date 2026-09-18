using System.Net;
using ActualChat.Module;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Core.Server.UnitTests;

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
    public async Task ReturnsRedirectAsIsWhenRedirectsAreOff()
    {
        // arrange
        var handler = new RedirectHandlerMock(_ => new (HttpStatusCode.Redirect) {
            Headers = {
                Location = new ("https://public.example/final"),
            },
        });
        var guard = NewGuard();
        var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress) {
            MaxRedirectCount = 0,
        };
        using var client = new HttpClient(new EgressHttpHandler(options, handler));

        // act
        using var response = await client.PostAsync("https://public.example/start", new StringContent("{}"));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        handler.RequestCount.Should().Be(1);
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
        var sut = NewGuard(new CoreServerSettings {
            EgressDomainDenylist = ["blocked.example"],
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

    [Theory]
    [InlineData("104.18.8.73", true)]
    [InlineData("::ffff:104.18.8.73", true)] // How a dual-mode socket reports the address above
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:169.254.0.1", false)]
    public void NormalizesIPv4MappedAddress(string address, bool isAllowed)
    {
        var result = _sut.IsAllowedAddress("api.example", IPAddress.Parse(address));
        result.Should().Be(isAllowed);
    }

    [Fact]
    public async Task DomainDenyListBlocksMatchingHost()
    {
        // arrange
        var sut = NewGuard(new CoreServerSettings {
            EgressDomainDenylist = ["actual.chat"],
            EgressCidrDenylist = ["10.0.0.0/8"],
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

    [Fact]
    public async Task CheckShouldTellUnresolvableFromDenied()
    {
        // arrange
        var sut = NewGuard(new CoreServerSettings {
            EgressDomainDenylist = ["blocked.example"],
            EgressCidrDenylist = ["8.8.0.0/16"],
        });

        // act
        var unresolvable = await sut.Check("no-such-host.invalid");
        var deniedIpLiteral = await sut.Check("10.0.0.1");
        var deniedDomain = await sut.Check("api.blocked.example");
        var deniedCidr = await sut.Check("dns.google");
        var allowed = await sut.Check("voxt.ai");

        // assert
        unresolvable.Should().Be(EgressVerdict.Unresolvable, ".invalid never resolves");
        deniedIpLiteral.Should().Be(EgressVerdict.Denied);
        deniedDomain.Should().Be(EgressVerdict.Denied);
        deniedCidr.Should().Be(EgressVerdict.Denied, "dns.google resolves into the denied 8.8.0.0/16");
        allowed.Should().Be(EgressVerdict.Allowed);
    }

    // Private methods

    private static EgressGuard NewGuard(CoreServerSettings? settings = null)
        => new (new HostInfo {
            Environment = Environments.Production,
            IsTested = true,
        },
        settings ?? new CoreServerSettings {
            EgressHostAllowList = ["public.example"],
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
}
