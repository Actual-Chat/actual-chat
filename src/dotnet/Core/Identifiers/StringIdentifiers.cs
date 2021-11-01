




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
    [JsonConverter(typeof(Internal.ChatIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.ChatIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.ChatIdTypeConverter))]
    public readonly partial struct ChatId : IEquatable<ChatId>, IIdentifier<string>
    {
        public static readonly ChatId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public ChatId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator ChatId(string value) => new(value);
        public static implicit operator string(ChatId id) => id.Value;

        // Equality
        public bool Equals(ChatId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
        public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class ChatIdJsonConverter : JsonConverter<ChatId>
    {
        public override ChatId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, ChatId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class ChatIdTypeConverter : TypeConverter
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
                return new ChatId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class ChatIdNewtonsoftJsonConverter : JsonConverter<ChatId>
    {
        public override void WriteJson(JsonWriter writer, ChatId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override ChatId ReadJson(
            JsonReader reader, Type objectType,
            ChatId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.AudioRecordIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.AudioRecordIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.AudioRecordIdTypeConverter))]
    public readonly partial struct AudioRecordId : IEquatable<AudioRecordId>, IIdentifier<string>
    {
        public static readonly AudioRecordId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public AudioRecordId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator AudioRecordId(string value) => new(value);
        public static implicit operator string(AudioRecordId id) => id.Value;

        // Equality
        public bool Equals(AudioRecordId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is AudioRecordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(AudioRecordId left, AudioRecordId right) => left.Equals(right);
        public static bool operator !=(AudioRecordId left, AudioRecordId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class AudioRecordIdJsonConverter : JsonConverter<AudioRecordId>
    {
        public override AudioRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, AudioRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class AudioRecordIdTypeConverter : TypeConverter
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
                return new AudioRecordId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class AudioRecordIdNewtonsoftJsonConverter : JsonConverter<AudioRecordId>
    {
        public override void WriteJson(JsonWriter writer, AudioRecordId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override AudioRecordId ReadJson(
            JsonReader reader, Type objectType,
            AudioRecordId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.VideoRecordIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.VideoRecordIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.VideoRecordIdTypeConverter))]
    public readonly partial struct VideoRecordId : IEquatable<VideoRecordId>, IIdentifier<string>
    {
        public static readonly VideoRecordId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public VideoRecordId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator VideoRecordId(string value) => new(value);
        public static implicit operator string(VideoRecordId id) => id.Value;

        // Equality
        public bool Equals(VideoRecordId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is VideoRecordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(VideoRecordId left, VideoRecordId right) => left.Equals(right);
        public static bool operator !=(VideoRecordId left, VideoRecordId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class VideoRecordIdJsonConverter : JsonConverter<VideoRecordId>
    {
        public override VideoRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, VideoRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class VideoRecordIdTypeConverter : TypeConverter
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
                return new VideoRecordId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class VideoRecordIdNewtonsoftJsonConverter : JsonConverter<VideoRecordId>
    {
        public override void WriteJson(JsonWriter writer, VideoRecordId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override VideoRecordId ReadJson(
            JsonReader reader, Type objectType,
            VideoRecordId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.StreamIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.StreamIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.StreamIdTypeConverter))]
    public readonly partial struct StreamId : IEquatable<StreamId>, IIdentifier<string>
    {
        public static readonly StreamId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public StreamId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator StreamId(string value) => new(value);
        public static implicit operator string(StreamId id) => id.Value;

        // Equality
        public bool Equals(StreamId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);
        public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class StreamIdJsonConverter : JsonConverter<StreamId>
    {
        public override StreamId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, StreamId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class StreamIdTypeConverter : TypeConverter
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
                return new StreamId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class StreamIdNewtonsoftJsonConverter : JsonConverter<StreamId>
    {
        public override void WriteJson(JsonWriter writer, StreamId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override StreamId ReadJson(
            JsonReader reader, Type objectType,
            StreamId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.UserIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.UserIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.UserIdTypeConverter))]
    public readonly partial struct UserId : IEquatable<UserId>, IIdentifier<string>
    {
        public static readonly UserId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public UserId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator UserId(string value) => new(value);
        public static implicit operator string(UserId id) => id.Value;

        // Equality
        public bool Equals(UserId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is UserId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(UserId left, UserId right) => left.Equals(right);
        public static bool operator !=(UserId left, UserId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class UserIdJsonConverter : JsonConverter<UserId>
    {
        public override UserId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, UserId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class UserIdTypeConverter : TypeConverter
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
                return new UserId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class UserIdNewtonsoftJsonConverter : JsonConverter<UserId>
    {
        public override void WriteJson(JsonWriter writer, UserId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override UserId ReadJson(
            JsonReader reader, Type objectType,
            UserId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}


namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(Internal.AuthorIdJsonConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(Internal.AuthorIdNewtonsoftJsonConverter))]
    [TypeConverter(typeof(Internal.AuthorIdTypeConverter))]
    public readonly partial struct AuthorId : IEquatable<AuthorId>, IIdentifier<string>
    {
        public static readonly AuthorId None = default;

        private readonly string? _value;

        [DataMember(Order = 0)]
        public string Value => _value ?? "";
        [IgnoreDataMember]
        public bool IsNone => Value.Length == 0;

        public AuthorId(string value) => _value = value ?? "";

        public override string ToString() => Value;

        public static implicit operator AuthorId(string value) => new(value);
        public static implicit operator string(AuthorId id) => id.Value;

        // Equality
        public bool Equals(AuthorId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is AuthorId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
        public static bool operator ==(AuthorId left, AuthorId right) => left.Equals(right);
        public static bool operator !=(AuthorId left, AuthorId right) => !left.Equals(right);
    }
}

namespace ActualChat.Internal
{
    public class AuthorIdJsonConverter : JsonConverter<AuthorId>
    {
        public override AuthorId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, AuthorId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class AuthorIdTypeConverter : TypeConverter
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
                return new AuthorId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }
}

namespace ActualChat.Internal
{
    using Newtonsoft.Json;

    public class AuthorIdNewtonsoftJsonConverter : JsonConverter<AuthorId>
    {
        public override void WriteJson(JsonWriter writer, AuthorId value, JsonSerializer serializer)
            => writer.WriteValue(value.Value);

        public override AuthorId ReadJson(
            JsonReader reader, Type objectType,
            AuthorId existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => new((string?) reader.Value!);
    }
}

