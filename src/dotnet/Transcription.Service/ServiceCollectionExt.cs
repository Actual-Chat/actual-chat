namespace ActualChat.Transcription;

public static class ServiceCollectionExt
{
    // Also called by the tests that construct SonioxOfflineTranscriber from a hand-built container
    public static IServiceCollection AddSoniox(this IServiceCollection services)
    {
        services.AddHttpClient(SonioxClient.HttpClientName);
        services.AddSingleton<SonioxClient>();
        services.AddSingleton(_ => new SonioxCleaner.Options());
        services.AddSingleton<SonioxCleaner>();
        return services;
    }
}
