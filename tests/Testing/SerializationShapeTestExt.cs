using System.Text.Json;
using MessagePack;
using Newtonsoft.Json;
using ActualLab.Serialization;

namespace ActualChat.Testing;

/// <summary>
/// Asserts a value's serialized shape — members emitted and their scalar values — is identical
/// across all three serializers, catching what <see cref="SerializationTestExt"/>'s round-trip
/// cannot: a member one serializer emits and another silently drops.
/// </summary>
public static class SerializationShapeTestExt
{
    private static readonly JsonSerializerSettings NewtonsoftSettings = CreateNewtonsoftSettings();

    public static T AssertSameShapeAcrossSerializers<T>(this T value, ITestOutputHelper? output = null)
    {
        var systemJson = Serializers.SystemJson.Write(value, typeof(T));
        var newtonsoftJson = new NewtonsoftJsonSerializer(NewtonsoftSettings).Write(value, typeof(T));
        using var buffer = Serializers.MessagePack.Write(value, typeof(T));
        var messagePackJson = MessagePackSerializer.ConvertToJson(
            buffer.WrittenMemory.ToArray(), MessagePackByteSerializer.DefaultOptions);
        output?.WriteLine($"SystemJson:  {systemJson}");
        output?.WriteLine($"Newtonsoft:  {newtonsoftJson}");
        output?.WriteLine($"MessagePack: {messagePackJson}");

        var name = typeof(T).GetName();
        var systemShape = Shape.Parse(systemJson);
        var newtonsoftShape = Shape.Parse(newtonsoftJson);
        var messagePackShape = Shape.Parse(messagePackJson);
        newtonsoftShape.Kind.Should().Be(systemShape.Kind,
            $"both JSON serializers must agree on how {name} is shaped");
        if (systemShape.Kind != JsonValueKind.Object)
            return value;

        newtonsoftShape.Names.Should().BeEquivalentTo(systemShape.Names,
            $"{name} must emit the same members to both JSON serializers");
        newtonsoftShape.Scalars.Should().BeEquivalentTo(systemShape.Scalars,
            $"{name} must emit the same values to both JSON serializers");

        // Integer-keyed MessagePack writes an array whose length is maxKey + 1, so a gap in the
        // [Key(N)] sequence legitimately makes it longer than the JSON member count.
        if (messagePackShape.Kind == JsonValueKind.Object) {
            messagePackShape.Names.Should().BeEquivalentTo(systemShape.Names,
                $"{name} must emit the same members to MessagePack");
        }
        else {
            messagePackShape.Count.Should().BeGreaterThanOrEqualTo(systemShape.Count,
                $"{name} emits {systemShape.Count} JSON members but only {messagePackShape.Count} " +
                $"MessagePack slots — a member is missing its [Key]");
        }

        // Values are compared by count, not equality: MessagePack legitimately encodes some
        // scalars differently, e.g. Moment is an ISO string in JSON but ticks here.
        messagePackShape.Scalars.Count.Should().Be(systemShape.Scalars.Count,
            $"{name} must emit as many values to MessagePack as to JSON — " +
            $"JSON has [{string.Join(", ", systemShape.Scalars)}], " +
            $"MessagePack has [{string.Join(", ", messagePackShape.Scalars)}]");

        return value;
    }

    // Private methods

    private static JsonSerializerSettings CreateNewtonsoftSettings()
    {
        // Production settings drop nulls, which would hide a member that exists but is unset.
        var settings = MemberwiseCloner.Invoke(NewtonsoftJsonSerializer.DefaultSettings);
        settings.NullValueHandling = NullValueHandling.Include;

        return settings;
    }

    // Nested types

    private sealed record Shape(
        JsonValueKind Kind,
        IReadOnlyList<string> Names,
        IReadOnlyList<string> Scalars,
        int Count)
    {
        public static Shape Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = Unwrap(document.RootElement);
            if (root.ValueKind == JsonValueKind.Object) {
                var properties = root.EnumerateObject()
                    .Where(p => !p.Name.StartsWith('$'))
                    .ToList();
                var names = properties.ConvertAll(p => p.Name.ToLower());
                var scalars = properties.Select(p => p.Value).Select(Scalar).SkipNullItems().ToList();
                return new Shape(root.ValueKind, names, scalars, properties.Count);
            }

            if (root.ValueKind == JsonValueKind.Array) {
                var items = root.EnumerateArray().ToList();
                var scalars = items.Select(Scalar).SkipNullItems().ToList();
                return new Shape(root.ValueKind, [], scalars, items.Count);
            }

            return new Shape(root.ValueKind, [], [], 0);
        }

        private static JsonElement Unwrap(JsonElement e)
            // MessagePack writes a [Union] value as a [unionKey, payload] envelope, which would
            // otherwise read as a 2-member type. JSON writes the payload's members directly.
            => e.ValueKind == JsonValueKind.Array
                && e.GetArrayLength() == 2
                && e[0].ValueKind == JsonValueKind.Number
                && e[1].ValueKind is JsonValueKind.Array or JsonValueKind.Object
                ? e[1]
                : e;

        private static string? Scalar(JsonElement e)
            // Only top-level scalars are compared: a nested value may legitimately differ in
            // representation, e.g. ApiOption<T> is {"hasValue":..} in JSON but [] in MessagePack.
            => e.ValueKind switch {
                JsonValueKind.String => "s:" + e.GetString(),
                JsonValueKind.True => "b:true",
                JsonValueKind.False => "b:false",
                JsonValueKind.Number => "n:" + e.GetDouble().ToString("R", null),
                _ => null,
            };
    }
}
