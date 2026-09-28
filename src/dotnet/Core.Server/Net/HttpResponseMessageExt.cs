namespace ActualChat;

public static class HttpResponseMessageExt
{
    public static TimeSpan? GetRetryAfter(this HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return delay > TimeSpan.Zero ? delay : null;
    }
}
