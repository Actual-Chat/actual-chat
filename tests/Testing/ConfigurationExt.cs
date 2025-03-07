using System.Linq.Expressions;
using ActualChat.Expressions;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Testing;

public static class ConfigurationExt
{
    public static IConfigurationBuilder AddInMemoryCollection(
        this IConfigurationBuilder builder,
        params (string Key, string? Value)[] values)
        => builder.AddInMemoryCollection(values.Select(x => KeyValuePair.Create(x.Key, x.Value)));

    public static IConfigurationBuilder AddInMemory<TSettings>(
        this IConfigurationBuilder builder,
        params (Expression<Func<TSettings, object>> PropertyGetter, string? Value)[] values)
        => builder.AddInMemoryCollection(values.ToDictionary(
            x => $"{typeof(TSettings).Name}:{x.PropertyGetter.GetPropertyName()}",
            x => x.Value,
            StringComparer.OrdinalIgnoreCase));
}
