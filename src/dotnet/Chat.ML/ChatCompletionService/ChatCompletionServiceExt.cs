using ActualChat.Redis;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public static class ChatCompletionServiceExt
{
    public static IChatCompletionService WrapWithRateLimiter(
        this IChatCompletionService chatCompletionService,
        RedisTokenBucketRateLimiter rateLimiter,
        string rateLimitKey)
        => new RateLimitedChatCompletionService(chatCompletionService, rateLimiter, rateLimitKey);
}
