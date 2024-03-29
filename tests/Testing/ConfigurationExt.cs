using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing;

public static class ConfigurationExt
{
    public static IConfigurationBuilder AddInMemoryCollection(
        this IConfigurationBuilder builder,
        params (string Key, string? Value)[] values)
        => builder.AddInMemoryCollection(values.Select(x => KeyValuePair.Create(x.Key, x.Value)));
}
