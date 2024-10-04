using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace ActualChat.Integrations.Anthropic;

public interface IAnthropicClient
{
    Task<string> Execute(string prompt, CancellationToken token);
}

internal sealed class AnthropicClientImpl(AnthropicClient anthropicClient) : IAnthropicClient
{
    public async Task<string> Execute(string prompt, CancellationToken token)
    {
        var parameters = new MessageParameters {
            Messages = [new Message(RoleType.User, prompt)],
            MaxTokens = 1024,
            Model = AnthropicModels.Claude3Haiku,
            Stream = false,
            Temperature = 0.01m,
        };

        var response = await anthropicClient.Messages
            .GetClaudeMessageAsync(parameters, null, token)
            .ConfigureAwait(false);
        return response.Message.ToString()!;
    }
}
