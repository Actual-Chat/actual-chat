using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ActualChat.Chat;

/// <summary>
/// Reads a Slack-compatible incoming webhook body: JSON, or a form with a <c>payload</c> field holding JSON.
/// </summary>
public static class InboundPayloadParser
{
    private const string JsonType = "application/json";
    private const string FormType = "application/x-www-form-urlencoded";

    public static InboundPayload? Parse(string contentType, string body)
    {
        var mediaType = contentType.Split(';')[0].Trim();
        if (mediaType.Equals(FormType, StringComparison.OrdinalIgnoreCase)) {
            var form = QueryHelpers.ParseQuery(body);
            if (!form.TryGetValue("payload", out var payload) || payload.Count == 0)
                return null;

            return ParseJson(payload[0] ?? "");
        }

        return mediaType.Equals(JsonType, StringComparison.OrdinalIgnoreCase) ? ParseJson(body) : null;
    }

    private static InboundPayload? ParseJson(string json)
    {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var cards = root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array
                ? attachments.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object).Select(ParseCard).ToArray()
                : [];
            return new InboundPayload(GetString(root, "text"), GetInt64(root, "replyTo"), cards);
        }
        catch (JsonException) {
            return null;
        }
    }

    private static InboundCard ParseCard(JsonElement card)
    {
        var fields = card.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new InboundField(GetString(x, "title"), GetString(x, "value")))
                .ToArray()
            : [];
        return new InboundCard(
            GetString(card, "title"), GetString(card, "title_link"), GetString(card, "pretext"), GetString(card, "text"),
            fields, GetString(card, "image_url"), GetString(card, "thumb_url"), GetString(card, "footer"));
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long? GetInt64(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : null;
}
