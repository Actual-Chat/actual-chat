using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public static class ConfigurationExt
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Covered by DynamicallyAccessedMemberTypes.All")]
    public static TSettings Settings<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TSettings>(
        this IConfiguration configuration,
        string? sectionName = null)
        where TSettings : class, new()
    {
        sectionName ??= typeof(TSettings).Name;
        var settings = new TSettings();
        configuration.GetSection(sectionName).Bind(settings);
        return settings;
    }
}
