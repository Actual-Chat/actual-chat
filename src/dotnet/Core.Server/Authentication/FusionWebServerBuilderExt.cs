using ActualLab.Fusion.Server;

namespace ActualChat.Authentication;

public static class FusionWebServerBuilderExt
{
    public static FusionWebServerBuilder AddAuthEndpoints(this FusionWebServerBuilder fusionWebServer)
    {
        var services = fusionWebServer.Services;
        services.AddSingleton(_ => AuthEndpoints.Options.Default);
        services.AddSingleton(c => new AuthEndpoints(c.GetRequiredService<AuthEndpoints.Options>()));
        return fusionWebServer;
    }

    public static FusionWebServerBuilder ConfigureAuthEndpoint(
        this FusionWebServerBuilder fusionWebServer,
        Func<IServiceProvider, AuthEndpoints.Options> optionsFactory)
    {
        fusionWebServer.Services.AddSingleton(optionsFactory);
        return fusionWebServer;
    }
}
