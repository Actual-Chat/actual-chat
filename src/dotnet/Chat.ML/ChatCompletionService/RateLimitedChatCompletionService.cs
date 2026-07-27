using ActualChat.Redis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public class RateLimitedChatCompletionService(
    IChatCompletionService chatCompletionService,
    RedisTokenBucketRateLimiter rateLimiter,
    string rateLimitKey)
    : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => ChatCompletionService.Attributes;
    public IChatCompletionService ChatCompletionService { get; } = chatCompletionService;
    private RedisTokenBucketRateLimiter RateLimiter { get; } = rateLimiter;
    private string RateLimitKey { get; } = rateLimitKey;

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var tokenCount = TokenEstimator.Estimate(chatHistory);
        await RateLimiter.Acquire(RateLimitKey, tokenCount, cancellationToken).ConfigureAwait(false);
        return await ChatCompletionService.GetChatMessageContentsAsync(chatHistory,
            executionSettings,
            kernel,
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tokenCount = TokenEstimator.Estimate(chatHistory);
        await RateLimiter.Acquire(RateLimitKey, tokenCount, cancellationToken).ConfigureAwait(false);
        var chatMessages = ChatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory,
            executionSettings,
            kernel,
            cancellationToken)
            .ConfigureAwait(false);
        await foreach (var chatMessage in chatMessages)
            yield return chatMessage;
    }
}
