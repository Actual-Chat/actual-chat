using ActualChat.Redis;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public static class ChatCompletionServiceExt
{
    public static IChatCompletionService WrapWithRateLimiter(
        this IChatCompletionService chatCompletionService,
        RedisTokenBucketRateLimiter rateLimiter)
        => new RateLimitedChatCompletionService(chatCompletionService, rateLimiter, new TokenEstimator());
}
