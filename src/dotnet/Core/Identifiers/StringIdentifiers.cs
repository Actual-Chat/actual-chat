using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
// ReSharper disable PartialTypeWithSinglePart
#pragma warning disable 618


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.IdentifierTestingIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.IdentifierTestingIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.IdentifierTestingIdTypeConverter))]
    public readonly partial struct IdentifierTestingId : IEquatable<IdentifierTestingId>, IIdentifier<string>
    {
        public static readonly IdentifierTestingId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public IdentifierTestingId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator IdentifierTestingId(string value) => new(value);
        public static implicit operator string(IdentifierTestingId id) => id.Value;

        // Equality
        public bool Equals(IdentifierTestingId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is IdentifierTestingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(IdentifierTestingId left, IdentifierTestingId right) => left.Equals(right);
        public static bool operator !=(IdentifierTestingId left, IdentifierTestingId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class IdentifierTestingIdJsonConverter : JsonConverter<IdentifierTestingId>
    {
        public override IdentifierTestingId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, IdentifierTestingId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class IdentifierTestingIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value!).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new IdentifierTestingId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class IdentifierTestingIdNewtonsoftJsonConverter : JsonConverter<IdentifierTestingId>
    {
        public override void WriteJson(JsonWriter writer, IdentifierTestingId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override IdentifierTestingId ReadJson(
            JsonReader reader, Type objectType,
            IdentifierTestingId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}
