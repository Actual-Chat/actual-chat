namespace ActualChat;

public static class EgressHttpClientExt
{
    public static IHttpClientBuilder AddEgressHttpClient(
        this IServiceCollection services, string name, long? maxResponseContentLength = null)
        => services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var guard = c.GetRequiredService<EgressGuard>();
                var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
                if (maxResponseContentLength is { } max)
                    options = options with { MaxResponseContentLength = max };
                return new EgressHttpHandler(options);
            });
}
