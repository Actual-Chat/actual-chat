using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public static class ConfigurationExt
{
    public static TSettings GetSettings<TSettings>(this IConfiguration configuration)
        where TSettings : class, new()
    {
        var settingsType = typeof(TSettings);
        var sectionName = settingsType.Name;
        var settings = new TSettings();
#pragma warning disable IL2026
        configuration.GetSection(sectionName)?.Bind(settings);
#pragma warning restore IL2026
        return settings;
    }
}
