using Microsoft.Extensions.Configuration;

namespace ActualChat.Configuration;

public static class ConfigurationExt
{
    public static void AddOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string path)
        where TOptions : class
#pragma warning disable IL2026
#pragma warning disable IL2091
        => services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(path))
            .ValidateDataAnnotations();
#pragma warning restore IL2091
#pragma warning restore IL2026
}
