using Anthropic.SDK;

namespace ActualChat.Integrations.Anthropic;

public static class ServiceCollectionExt
{
    public static void AddAnthropicServices(this IServiceCollection services)
    {
        services.AddSingleton<IPromptUtils, PromptUtils>();
        services.AddSingleton<IAnthropicClient, AnthropicClientImpl>();
        services.AddSingleton<AnthropicClient>();
    }
}
