using System.Text;
using Android.Content;
using Android.OS;

namespace ActualChat.App.Maui;

public static class Formatters
{
    public static string DumpIntent(Intent? intent)
    {
        if (intent == null)
            return "<null/>";

        try {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString(nameof(Intent.Action), intent.Action);
            WriteStringArray(intent, writer);
            writer.WriteString(nameof(Intent.Flags), intent.Flags.ToString());
            WriteBundle(writer, nameof(Intent.Extras), intent.Extras);
            writer.WriteEndObject();
            writer.Flush();
            var json = Encoding.UTF8.GetString(stream.ToArray());
            return json;
        }
        catch (Exception e) {
            return $"<error>{e.Message}</error>";
        }
    }

    private static void WriteStringArray(Intent intent, Utf8JsonWriter writer)
    {
        if (intent.Categories == null)
            writer.WriteNull(nameof(Intent.Categories));
        else {
            writer.WriteStartArray(nameof(Intent.Categories));
            foreach (var category in intent.Categories!)
                writer.WriteStringValue(category);
            writer.WriteEndArray();
        }
    }

    private static void WriteBundle(Utf8JsonWriter writer, string propertyName, Bundle? bundle)
    {
        if (bundle == null)
            writer.WriteNull(propertyName);
        else {
            writer.WritePropertyName(propertyName);
            WriteBundle(writer, bundle);
        }
    }

    private static void WriteBundle(Utf8JsonWriter writer, Bundle bundle)
    {
        writer.WriteStartObject();
        foreach (var key in bundle.KeySet()!)
            try {
                // NOTE(DF): Use try/catch because Get method is deprecated. What to use for replacement?
                var value = bundle.Get(key);
                writer.WriteString(key, value?.ToString());
            }
            catch (Exception e) {
                writer.WriteString(key, $"<error>{e.Message}</error>");
            }
        writer.WriteEndObject();
    }
}
