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
        => builder.AddInMemory(",", values);

    public static IConfigurationBuilder AddInMemory<TSettings>(
        this IConfigurationBuilder builder,
        string listSeparator,
        params (Expression<Func<TSettings, object>> PropertyGetter, string? Value)[] values)
        => builder.AddInMemoryCollection(Materialize(listSeparator, values));

    private static IEnumerable<KeyValuePair<string, string?>> Materialize<TSettings>(
        string listSeparator,
        (Expression<Func<TSettings, object>> PropertyGetter, string? Value)[] values)
        => values.SelectMany(x => Materialize(listSeparator, x.PropertyGetter, x.Value));

    private static IEnumerable<KeyValuePair<string, string?>> Materialize<TSettings>(string listSeparator, Expression<Func<TSettings, object>> propertyGetter, string? value)
    {
        var baseKey = $"{typeof(TSettings).Name}:{propertyGetter.GetPropertyName()}";
        var propertyType = propertyGetter.GetPropertyType();
        if (!propertyType.IsAssignableTo(typeof(IEnumerable<string>)))
            return [KeyValuePair.Create(baseKey, value)];

        return value?.Split(listSeparator, StringSplitOptions.TrimEntries)
                .Select((v, i) => KeyValuePair.Create($"{baseKey}:{i}", (string?)v))
            ?? [];
    }
}
