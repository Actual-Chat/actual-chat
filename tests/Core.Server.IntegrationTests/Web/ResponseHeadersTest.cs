using ActualChat.App.Server.Module;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActualChat.Core.Server.IntegrationTests.Web;

public class ResponseHeadersTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ResponseHeadersTest)}", Options, @out)
{
    private const string HtmlPath = "test/response-headers-page";

    private static readonly TestAppHostOptions Options = TestAppHostOptions.Default with {
        ConfigureApp = (_, app) => app.MapGet($"/{HtmlPath}",
            (HttpContext httpContext) => Results.Content(
                $"<html lang=\"en\"><body>{ContentSecurityPolicy.GetNonce(httpContext)}</body></html>",
                "text/html")),
    };

    [Fact]
    public async Task SendsFramingAndSniffingHeaders()
    {
        // arrange
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync("rpc/check");

        // assert
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task SendsContentSecurityPolicyForPages()
    {
        // arrange
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync(HtmlPath);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Out.WriteLine(policy);

        // assert
        policy.Should().Contain("default-src 'self'");
        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("base-uri 'self'");
        policy.Should().Contain("form-action 'self'");
        policy.Should().Contain("object-src 'none'");
    }

    [Fact]
    public async Task UsesSameNonceInPolicyAndPage()
    {
        // arrange
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync(HtmlPath);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var html = await response.Content.ReadAsStringAsync();
        var pageNonce = html.Replace("<html lang=\"en\"><body>", "").Replace("</body></html>", "");

        // assert
        pageNonce.Should().NotBeEmpty();
        policy.Should().Contain($"'nonce-{pageNonce}'");
    }

    [Fact]
    public async Task SkipsContentSecurityPolicyForNonPages()
    {
        // arrange
        await using var host = await NewAppHost();
        using var httpClient = host.NewHttpClient();

        // act
        using var response = await httpClient.GetAsync("rpc/check");

        // assert
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }
}
