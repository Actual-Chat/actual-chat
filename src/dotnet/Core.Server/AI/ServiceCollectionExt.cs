using ActualChat.Module;
using Anthropic.SDK;

namespace ActualChat.AI;

public static class ServiceCollectionExt
{
    public static void AddAIServices(this IServiceCollection services)
    {
        services.AddSingleton<IPromptHelpers, PromptHelpers>();
        services.AddSingleton<IAnthropicClient, AnthropicClientWrapper>();
        services.AddSingleton(c => {
            // Anthropic.SDK falls back to the ANTHROPIC_API_KEY env var when constructed without one.
            var key = c.GetRequiredService<CoreServerSettings>().AnthropicKey;
            return key.IsNullOrEmpty() ? new AnthropicClient() : new AnthropicClient(key);
        });
    }
}
