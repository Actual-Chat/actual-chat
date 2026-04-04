using Microsoft.Extensions.Configuration;

namespace ActualChat.Configuration;

public static class ConfigurationExt
{
    public static void AddOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string path)
        where TOptions : class
        => services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(path))
            .ValidateDataAnnotations();
}
