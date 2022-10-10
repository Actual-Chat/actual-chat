using System.ComponentModel;

// ReSharper disable PartialTypeWithSinglePart
#pragma warning disable 618


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.TestStringIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.TestStringIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.TestStringIdTypeConverter))]
    public readonly partial struct TestStringId : IEquatable<TestStringId>, IIdentifier<string>
    {
        public static readonly TestStringId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public TestStringId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator TestStringId(string value) => new(value);
        public static implicit operator string(TestStringId id) => id.Value;

        // Equality
        public bool Equals(TestStringId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object? obj) => obj is TestStringId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(TestStringId left, TestStringId right) => left.Equals(right);
        public static bool operator !=(TestStringId left, TestStringId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class TestStringIdJsonConverter : JsonConverter<TestStringId>
    {
        public override TestStringId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, TestStringId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class TestStringIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>)value!).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new TestStringId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class TestStringIdNewtonsoftJsonConverter : JsonConverter<TestStringId>
    {
        public override void WriteJson(JsonWriter writer, TestStringId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override TestStringId ReadJson(
            JsonReader reader, Type objectType,
            TestStringId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?)reader.Value!);
    }
}

namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.LanguageIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.LanguageIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.LanguageIdTypeConverter))]
    public readonly partial struct LanguageId : IEquatable<LanguageId>, IIdentifier<string>
    {
        public static readonly LanguageId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public LanguageId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator LanguageId(string value) => new(value);
        public static implicit operator string(LanguageId id) => id.Value;

        // Equality
        public bool Equals(LanguageId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object? obj) => obj is LanguageId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(LanguageId left, LanguageId right) => left.Equals(right);
        public static bool operator !=(LanguageId left, LanguageId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class LanguageIdJsonConverter : JsonConverter<LanguageId>
    {
        public override LanguageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, LanguageId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class LanguageIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>)value!).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new LanguageId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class LanguageIdNewtonsoftJsonConverter : JsonConverter<LanguageId>
    {
        public override void WriteJson(JsonWriter writer, LanguageId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override LanguageId ReadJson(
            JsonReader reader, Type objectType,
            LanguageId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?)reader.Value!);
    }
}
