namespace ActualChat;

public static class EgressHttpClientExt
{
    public static IHttpClientBuilder AddEgressHttpClient(
        this IServiceCollection services,
        string name,
        long? maxResponseContentLength = null,
        int? maxRedirectCount = null)
        => services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var guard = c.GetRequiredService<EgressGuard>();
                var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
                if (maxResponseContentLength is { } max)
                    options = options with { MaxResponseContentLength = max };
                if (maxRedirectCount is { } redirectCount)
                    options = options with { MaxRedirectCount = redirectCount };
                return new EgressHttpHandler(options);
            });
}
