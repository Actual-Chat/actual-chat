using ActualChat.AI;
using ActualChat.Chat.Module;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ActualChat.Chat;

public abstract class ChatCompletionBasedService(IServiceProvider services, string serviceKey) : IHasServices
{
    public IServiceProvider Services => services;
    [field: AllowNull, MaybeNull]
    private Kernel Kernel => field ??= Services.GetRequiredService<Kernel>();
    [field: AllowNull, MaybeNull]
    protected IChatCompletionService Completion => field ??= Kernel.GetRequiredService<IChatCompletionService>(serviceKey);
    [field: AllowNull, MaybeNull]
    protected ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    protected IPromptUtils PromptUtils => field ??= Services.GetRequiredService<IPromptUtils>();
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());

    protected async Task<string> Ask(string instruction, string text, CancellationToken cancellationToken)
    {
        var history = new ChatHistory(new ChatMessageContent[] {
            new (AuthorRole.User, text),
        }.Where(x => !x.Content.IsNullOrEmpty()));
        var response = await Completion
            .GetChatMessageContentAsync(
                history,
                new OpenAIPromptExecutionSettings {
                    Temperature = 0,
                    ChatSystemPrompt = instruction.Trim().EnsureSuffix(":"),
                },
                Kernel,
                cancellationToken)
            .ConfigureAwait(false);
        return response.Content ?? "";
    }
}
