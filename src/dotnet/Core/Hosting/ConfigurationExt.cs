using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public static class ConfigurationExt
{
    public static TSettings Settings<TSettings>(this IConfiguration configuration, string? sectionName = null)
        where TSettings : class, new()
    {
        sectionName ??= typeof(TSettings).Name;
        var settings = new TSettings();
#pragma warning disable IL2026
        configuration.GetSection(sectionName)?.Bind(settings);
#pragma warning restore IL2026
        return settings;
    }
}
