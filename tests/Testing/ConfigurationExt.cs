using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing;

public static class ConfigurationExt
{
    public static IConfigurationBuilder AddInMemory(this IConfigurationBuilder builder, params (string Key, string? Value)[] values)
        => builder.AddInMemoryCollection(values.ToDictionary(x => x.Key, x => x.Value));
}
