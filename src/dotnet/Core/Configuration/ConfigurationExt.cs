using Microsoft.Extensions.Configuration;

namespace ActualChat.Configuration;

public static class ConfigurationExt
{
    public static void AddOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string path)
        where TOptions : class
 #pragma warning disable IL2026
        => services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(path))
 #pragma warning restore IL2026
            .ValidateDataAnnotations();
}
