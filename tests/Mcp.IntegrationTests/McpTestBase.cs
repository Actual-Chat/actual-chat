using ActualChat.OAuth;
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
        return await CreateClientWithRawKey(apiKey, ct: ct).ConfigureAwait(false);
    }

    protected async Task<McpClient> CreateClientWithRawKey(
        string sessionId, string route = OAuthConstants.McpResourcePath, CancellationToken ct = default)
    {
        var baseUri = Tester.UrlMapper.BaseUri;
        var endpoint = new Uri(baseUri, route);
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = endpoint,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {sessionId}" },
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
    }

    protected static async Task<T> CallTool<T>(McpClient client, string name, object? args = null)
    {
        var result = await client.CallToolAsync(name, ToArguments(args)).ConfigureAwait(false);
        return DeserializeResult<T>(result);
    }

    protected static async Task CallTool(McpClient client, string name, object? args = null)
    {
        var result = await client.CallToolAsync(name, ToArguments(args)).ConfigureAwait(false);
        if (result.IsError == true)
            throw new InvalidOperationException("Tool returned an error: " + ErrorText(result));
    }

    protected static async Task<string> CallToolExpectingError(McpClient client, string name, object? args = null)
    {
        var result = await client.CallToolAsync(name, ToArguments(args)).ConfigureAwait(false);
        result.IsError.Should().BeTrue($"'{name}' must fail");
        return ErrorText(result);
    }

    protected static async Task<T> WaitFor<T>(Func<Task<T>> read, Func<T, bool> isReady, int attempts = 30)
    {
        var result = await read().ConfigureAwait(false);
        for (var i = 1; i < attempts && !isReady(result); i++) {
            await Task.Delay(100).ConfigureAwait(false);
            result = await read().ConfigureAwait(false);
        }
        return result;
    }

    protected static T DeserializeResult<T>(CallToolResult result)
    {
        if (result.IsError == true)
            throw new InvalidOperationException("Tool returned an error: " + ErrorText(result));
        var json = result.StructuredContent
            ?? throw new InvalidOperationException("Tool result has no structured content.");
        var options = SystemJsonSerializer.Default.Options;
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("result", out var inner))
            return inner.Deserialize<T>(options)
                ?? throw new InvalidOperationException("Tool result could not be deserialized.");
        return json.Deserialize<T>(options)
            ?? throw new InvalidOperationException("Tool result could not be deserialized.");
    }

    private static string ErrorText(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private static Dictionary<string, object?>? ToArguments(object? args)
    {
        if (args is null)
            return null;
        if (args is Dictionary<string, object?> dictionary)
            return dictionary;

        return args.GetType()
            .GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(args));
    }
}
