using System.Text.Json;

namespace ActualChat.Serialization;

/// <summary>
/// Persists a <see cref="MetadataBag"/> as <c>{"Items":{...}}</c> - the shape the legacy
/// ImmutableOptionSet wrote. Nothing here emits a type name, so nothing here resolves one;
/// the bag's schema is what keeps the values primitive.
/// </summary>
public static class MetadataBagJson
{
    public static MetadataBag FromJson(string? json)
    {
        if (json.IsNullOrEmpty())
            return MetadataBag.Empty;

        // A row we can't read costs its owner a broken avatar or attachment, so never throw over one
        Envelope? envelope;
        try {
            envelope = JsonSerializer.Deserialize<Envelope>(json, SystemJsonSerializer.Default.Options);
        }
        catch (JsonException) {
            return MetadataBag.Empty;
        }

        if (envelope?.Items is not { Count: > 0 } items)
            return MetadataBag.Empty;

        var result = MetadataBag.Empty;
        foreach (var (key, item) in items)
            result = ToValue(item) is { } value ? result.Set(key, value) : result;

        return result;
    }

    public static string ToJson(this MetadataBag bag)
    {
        if (bag.Count == 0)
            return "";

        var items = new Dictionary<string, object>();
        foreach (var item in bag.Items)
            items.Add(item.Key, item.Value);

        return JsonSerializer.Serialize(new Envelope { Items = items }, SystemJsonSerializer.Default.Options);
    }

    // Private methods

    private static object? ToValue(object? item)
    {
        // System.Text.Json binds an object-typed value to JsonElement rather than to a CLR primitive
        if (item is not JsonElement element)
            return item;

        // The (object) cast keeps a whole number a long: without it the ternary unifies both arms to double
        return element.ValueKind switch {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    // Nested types

    private sealed class Envelope
    {
        // Pinned: the shared options camel-case by default, and this name is the stored format
        [JsonPropertyName("Items")]
        public Dictionary<string, object>? Items { get; init; }
    }
}
