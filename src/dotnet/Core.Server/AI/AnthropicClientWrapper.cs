using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace ActualChat.AI;

public interface IAnthropicClient
{
    Task<string> Execute(string prompt, CancellationToken cancellationToken);
}

internal sealed class AnthropicClientWrapper(AnthropicClient anthropicClient) : IAnthropicClient
{
    public async Task<string> Execute(string prompt, CancellationToken cancellationToken)
    {
        var parameters = new MessageParameters {
            Messages = [new Message(RoleType.User, prompt)],
            MaxTokens = 1024,
            Model = AnthropicModels.Claude45Haiku,
            Stream = false,
            Temperature = 0.01m,
        };

        var response = await anthropicClient.Messages
            .GetClaudeMessageAsync(parameters, cancellationToken)
            .ConfigureAwait(false);
        return response.Message.ToString()!;
    }
}
