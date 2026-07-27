using System.Net;

namespace ActualChat.Kubernetes.IntegrationTests;

// Talks to the real deployed environments, so it is opt-in: set AC_TEST_LIVE_INGRESS
// to run it, e.g. as a post-deploy step. Left in the normal lane it would make CI
// depend on production being reachable.
public sealed class IngressRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string EnableVariable = "AC_TEST_LIVE_INGRESS";
    private static readonly string[] BackendRoutes = ["/backend/rpc/http", "/backend/rpc/ws"];

    [Theory]
    [InlineData("https://voxt.ai")]
    [InlineData("https://actual.chat")]
    [InlineData("https://dev.voxt.ai")]
    public async Task BackendRoutesReturnNotFound(string baseUrl)
    {
        // xunit 2.9 has no dynamic skip, so a green run without the request lines means "not run".
        if (Environment.GetEnvironmentVariable(EnableVariable).IsNullOrEmpty()) {
            WriteLine($"NOT RUN: set {EnableVariable} to check deployed environments.");
            return;
        }

        // arrange
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // act
        // The control runs first and is load-bearing: it proves requests reach the live
        // RPC endpoint. Without it an unreachable host, or an ingress that answers 404 for
        // everything, would satisfy the assertions below while telling us nothing.
        var control = await GetStatus(client, HttpMethod.Post, $"{baseUrl}/rpc/http");
        var results = new List<(string Url, HttpMethod Method, HttpStatusCode Status)>();
        foreach (var route in BackendRoutes) {
            var url = baseUrl + route;
            results.Add((url, HttpMethod.Post, await GetStatus(client, HttpMethod.Post, url)));
            results.Add((url, HttpMethod.Get, await GetStatus(client, HttpMethod.Get, url)));
        }

        // assert
        control.Should().NotBe(HttpStatusCode.NotFound,
            $"POST {baseUrl}/rpc/http must reach the RPC endpoint, otherwise this test cannot "
            + "distinguish a working deny from an unreachable host");
        foreach (var (url, method, status) in results)
            status.Should().Be(HttpStatusCode.NotFound, $"{method} {url} must not be routed");
    }

    // Private methods

    private async Task<HttpStatusCode> GetStatus(HttpClient client, HttpMethod method, string url)
    {
        using var request = new HttpRequestMessage(method, url);
        if (method == HttpMethod.Post)
            request.Content = new ByteArrayContent([]);
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        WriteLine($"{method} {url} -> {(int)response.StatusCode}");
        return response.StatusCode;
    }
}
