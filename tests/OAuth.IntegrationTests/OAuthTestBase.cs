using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Client;

namespace ActualChat.OAuth.IntegrationTests;

public abstract class OAuthTestBase<TFixture>(TFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TFixture>(fixture, @out)
    where TFixture : AppHostFixture
{
    protected WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    protected Uri BaseUri => Tester.UrlMapper.BaseUri;
    protected HttpClient Http => field ??= new(new HttpClientHandler {
        AllowAutoRedirect = false,
        UseCookies = false,
    }) {
        BaseAddress = BaseUri,
    };

    protected override async Task DisposeAsync()
    {
        Http.Dispose();
        await Tester.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected async Task<JsonElement> GetJson(string path)
    {
        var response = await Http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    protected async Task<string> RegisterClient(params string[] redirectUris)
    {
        var (_, doc) = await Register(new {
            client_name = "Test Client",
            redirect_uris = redirectUris,
            grant_types = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_method = "none",
        });
        return doc.GetProperty("client_id").GetString()!;
    }

    protected async Task<(HttpStatusCode, JsonElement)> Register(object body)
    {
        var response = await Http.PostAsJsonAsync("/oauth/register", body);
        var json = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(json).RootElement);
    }

    protected static Pkce NewPkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new Pkce(verifier, challenge);
    }

    protected static string AuthorizeUrl(
        string clientId, string redirectUri, Pkce pkce, string? state, string scope, string? resource)
    {
        var query = new Dictionary<string, string?> {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
            ["resource"] = resource,
        };
        return QueryHelpers.AddQueryString("/oauth/authorize", query.Where(kv => kv.Value is not null));
    }

    protected async Task<HttpResponseMessage> Authorize(
        string clientId, string redirectUri, Pkce pkce, string? state = "s1",
        string scope = "mcp offline_access", string? resource = null, bool approve = true)
    {
        // Drives the consent step the way the Blazor page will: approve via the command, then re-request
        // /oauth/authorize. Returns the final response, whose Location is the client redirect.
        var url = AuthorizeUrl(clientId, redirectUri, pkce, state, scope, resource);
        var first = await SendAsUser(HttpMethod.Get, url);
        if (first.StatusCode != HttpStatusCode.Redirect
            || !first.Headers.Location!.ToString().Contains(OAuthConstants.ConsentPath))
            return first;
        if (!approve)
            return await SendAsUser(HttpMethod.Get, $"{url}&{OAuthConstants.DenyParameter}=1");

        await Tester.Commander.Call(new OAuthGrants_Approve {
            Session = Tester.Session,
            ClientId = clientId,
            Scopes = scope.Split(' ').ToApiArray(),
        });
        return await SendAsUser(HttpMethod.Get, url);
    }

    protected Task<HttpResponseMessage> SendAsUser(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"{Constants.Session.CookieName}={Tester.Session.Id}");
        return Http.SendAsync(request);
    }

    protected static string GetQueryValue(Uri location, string name)
        => QueryHelpers.ParseQuery(location.Query).TryGetValue(name, out var value) ? value.ToString() : "";

    protected async Task<(HttpStatusCode, JsonElement)> PostToken(Dictionary<string, string> form)
    {
        var response = await Http.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
    }

    protected Task<(HttpStatusCode, JsonElement)> ExchangeCode(
        string clientId, string redirectUri, string code, string verifier, string? resource = null)
    {
        var form = new Dictionary<string, string> {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        };
        if (resource is not null)
            form["resource"] = resource;
        return PostToken(form);
    }

    protected Task<(HttpStatusCode, JsonElement)> Refresh(string clientId, string refreshToken)
        => PostToken(new() {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
        });

    protected async Task<(string ClientId, JsonElement Tokens)> ConnectClient(
        string redirectUri = "https://c.example/cb")
    {
        // One-call happy path: register, consent, exchange the code
        var clientId = await RegisterClient(redirectUri);
        var pkce = NewPkce();
        var response = await Authorize(clientId, redirectUri, pkce);
        var code = GetQueryValue(response.Headers.Location!, "code");
        var (_, tokens) = await ExchangeCode(clientId, redirectUri, code, pkce.Verifier);
        return (clientId, tokens);
    }

    protected static JwtSecurityToken ReadJwt(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token);

    protected async Task<McpClient> CreateMcpClient(string token, CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(BaseUri, "/api/mcp");
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = endpoint,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> SendInitialize(string? authorization)
    {
        using var http = Tester.AppHost.NewHttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "/api/mcp"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return await http.SendAsync(request).ConfigureAwait(false);
    }

    // Nested types

    protected sealed record Pkce(string Verifier, string Challenge);
}
