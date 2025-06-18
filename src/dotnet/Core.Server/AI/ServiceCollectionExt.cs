using Anthropic.SDK;

namespace ActualChat.AI;

public static class ServiceCollectionExt
{
    public static void AddAIServices(this IServiceCollection services)
    {
        services.AddSingleton<IPromptHelpers, PromptHelpers>();
        services.AddSingleton<IAnthropicClient, AnthropicClientWrapper>();
        services.AddSingleton<AnthropicClient>();
    }
}
