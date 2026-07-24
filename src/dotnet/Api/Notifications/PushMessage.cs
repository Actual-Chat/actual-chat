namespace ActualChat.Notifications;

/// <summary>
/// JSON wire form of one <see cref="NotificationMessage"/> in the push payload's "messages" data
/// key (oldest -> newest); short property names keep the FCM message under its 4KB limit.
/// </summary>
public sealed record PushMessage(
    [property: JsonPropertyName("n")] string AuthorName,
    [property: JsonPropertyName("t")] string Text,
    [property: JsonPropertyName("ts")] long SentAtMs)
{
    public const int MaxJsonLength = 2500;

    public static List<PushMessage> From(ApiArray<NotificationMessage> messages)
        => messages
            .Select(m => new PushMessage(m.AuthorName, m.Text, (long)m.SentAt.EpochOffset.TotalMilliseconds))
            .ToList();

    public static string ToJson(ApiArray<NotificationMessage> messages, int maxLength = MaxJsonLength)
    {
        // Drops oldest messages while the JSON exceeds the budget; returns "" when even the newest
        // alone can't fit, so this key alone can't push the whole FCM message over its 4KB limit.
        var items = From(messages);
        var json = JsonSerializer.Serialize(items);
        while (items.Count > 1 && json.Length > maxLength) {
            items.RemoveAt(0);
            json = JsonSerializer.Serialize(items);
        }
        return json.Length > maxLength ? "" : json;
    }

    public static IReadOnlyList<PushMessage> FromJson(string? json)
    {
        if (json.IsNullOrEmpty())
            return [];

        try {
            return JsonSerializer.Deserialize<List<PushMessage>>(json) ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }
}
