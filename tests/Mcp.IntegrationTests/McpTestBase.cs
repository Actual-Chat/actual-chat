using ActualChat.Testing.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ActualChat.Mcp.IntegrationTests;

public abstract class McpTestBase<TFixture>(TFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TFixture>(fixture, @out)
    where TFixture : AppHostFixture
{
    protected WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected async Task<string> IssueApiKey(string name = "test", CancellationToken ct = default)
    {
        var command = new Accounts_CreateApiKey { Session = Tester.Session, Name = name };
        return await Tester.Commander.Call(command, ct).ConfigureAwait(false);
    }

    protected async Task<McpClient> CreateClient(string? apiKey = null, CancellationToken ct = default)
    {
        apiKey ??= await IssueApiKey(ct: ct).ConfigureAwait(false);
        return await CreateClientWithRawKey(apiKey, ct).ConfigureAwait(false);
    }

    protected async Task<McpClient> CreateClientWithRawKey(string sessionId, CancellationToken ct = default)
    {
        var baseUri = Tester.UrlMapper.BaseUri;
        var endpoint = new Uri(baseUri, "/api/mcp");
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = endpoint,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {sessionId}" },
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
    }

    protected static T DeserializeResult<T>(CallToolResult result)
    {
        if (result.IsError == true)
            throw new InvalidOperationException(
                "Tool returned an error: "
                + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
        var json = result.StructuredContent
            ?? throw new InvalidOperationException("Tool result has no structured content.");
        var options = SystemJsonSerializer.Default.Options;
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("result", out var inner))
            return inner.Deserialize<T>(options)
                ?? throw new InvalidOperationException("Tool result could not be deserialized.");
        return json.Deserialize<T>(options)
            ?? throw new InvalidOperationException("Tool result could not be deserialized.");
    }
}
