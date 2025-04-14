using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace ActualChat.AI;

public interface IAnthropicClient
{
    Task<string> Execute(string prompt, CancellationToken token);
}

internal sealed class AnthropicClientWrapper(AnthropicClient anthropicClient) : IAnthropicClient
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
            .GetClaudeMessageAsync(parameters, token)
            .ConfigureAwait(false);
        return response.Message.ToString()!;
    }
}
