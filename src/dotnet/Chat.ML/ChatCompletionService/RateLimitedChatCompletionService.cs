using ActualChat.Redis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public class RateLimitedChatCompletionService(
    IChatCompletionService chatCompletionService,
    RedisTokenBucketRateLimiter rateLimiter,
    TokenEstimator estimator)
    : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => chatCompletionService.Attributes;
    public IChatCompletionService ChatCompletionService => chatCompletionService;

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var tokens = await estimator.Estimate(chatHistory, cancellationToken).ConfigureAwait(false);
        await rateLimiter.Acquire(tokens, cancellationToken).ConfigureAwait(false);
        return await chatCompletionService.GetChatMessageContentsAsync(chatHistory,
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
        var tokens = await estimator.Estimate(chatHistory, cancellationToken).ConfigureAwait(false);
        await rateLimiter.Acquire(tokens, cancellationToken).ConfigureAwait(false);
        var chatMessages = chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory,
            executionSettings,
            kernel,
            cancellationToken)
            .ConfigureAwait(false);
        await foreach (var chatMessage in chatMessages)
            yield return chatMessage;
    }
}
