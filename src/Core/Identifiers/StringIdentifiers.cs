using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stl.Text;
// ReSharper disable PartialTypeWithSinglePart
#pragma warning disable 618

namespace ActualChat
{
    [DataContract]
    [JsonConverter(typeof(ChatIdJsonConverter))]
    [TypeConverter(typeof(ChatIdTypeConverter))]
    public readonly partial struct ChatId : IEquatable<ChatId>, IIdentifier<string>
    {
        [Obsolete("Use implicit cast to/from string instead")]
        [DataMember(Order = 0)]
        public string Value { get; }

        public ChatId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator ChatId(string value) => new(value);
        public static implicit operator string(ChatId id) => id.Value;

        // Equality
        public bool Equals(ChatId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
        public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);
    }

    public class ChatIdJsonConverter : JsonConverter<ChatId>
    {
        public override ChatId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, ChatId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class ChatIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new ChatId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }


    [DataContract]
    [JsonConverter(typeof(AudioRecordIdJsonConverter))]
    [TypeConverter(typeof(AudioRecordIdTypeConverter))]
    public readonly partial struct AudioRecordId : IEquatable<AudioRecordId>, IIdentifier<string>
    {
        [Obsolete("Use implicit cast to/from string instead")]
        [DataMember(Order = 0)]
        public string Value { get; }

        public AudioRecordId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator AudioRecordId(string value) => new(value);
        public static implicit operator string(AudioRecordId id) => id.Value;

        // Equality
        public bool Equals(AudioRecordId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AudioRecordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(AudioRecordId left, AudioRecordId right) => left.Equals(right);
        public static bool operator !=(AudioRecordId left, AudioRecordId right) => !left.Equals(right);
    }

    public class AudioRecordIdJsonConverter : JsonConverter<AudioRecordId>
    {
        public override AudioRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, AudioRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class AudioRecordIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new AudioRecordId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }


    [DataContract]
    [JsonConverter(typeof(VideoRecordIdJsonConverter))]
    [TypeConverter(typeof(VideoRecordIdTypeConverter))]
    public readonly partial struct VideoRecordId : IEquatable<VideoRecordId>, IIdentifier<string>
    {
        [Obsolete("Use implicit cast to/from string instead")]
        [DataMember(Order = 0)]
        public string Value { get; }

        public VideoRecordId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator VideoRecordId(string value) => new(value);
        public static implicit operator string(VideoRecordId id) => id.Value;

        // Equality
        public bool Equals(VideoRecordId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is VideoRecordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(VideoRecordId left, VideoRecordId right) => left.Equals(right);
        public static bool operator !=(VideoRecordId left, VideoRecordId right) => !left.Equals(right);
    }

    public class VideoRecordIdJsonConverter : JsonConverter<VideoRecordId>
    {
        public override VideoRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, VideoRecordId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class VideoRecordIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new VideoRecordId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }


    [DataContract]
    [JsonConverter(typeof(StreamIdJsonConverter))]
    [TypeConverter(typeof(StreamIdTypeConverter))]
    public readonly partial struct StreamId : IEquatable<StreamId>, IIdentifier<string>
    {
        [Obsolete("Use implicit cast to/from string instead")]
        [DataMember(Order = 0)]
        public string Value { get; }

        public StreamId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator StreamId(string value) => new(value);
        public static implicit operator string(StreamId id) => id.Value;

        // Equality
        public bool Equals(StreamId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);
        public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);
    }

    public class StreamIdJsonConverter : JsonConverter<StreamId>
    {
        public override StreamId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, StreamId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class StreamIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new StreamId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }


    [DataContract]
    [JsonConverter(typeof(UserIdJsonConverter))]
    [TypeConverter(typeof(UserIdTypeConverter))]
    public readonly partial struct UserId : IEquatable<UserId>, IIdentifier<string>
    {
        [Obsolete("Use implicit cast to/from string instead")]
        [DataMember(Order = 0)]
        public string Value { get; }

        public UserId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator UserId(string value) => new(value);
        public static implicit operator string(UserId id) => id.Value;

        // Equality
        public bool Equals(UserId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is UserId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(UserId left, UserId right) => left.Equals(right);
        public static bool operator !=(UserId left, UserId right) => !left.Equals(right);
    }

    public class UserIdJsonConverter : JsonConverter<UserId>
    {
        public override UserId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, UserId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    public class UserIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
                return ((IIdentifier<string>) value).Value;
            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                // ReSharper disable once HeapView.BoxingAllocation
                return new UserId(s);
            return base.ConvertFrom(context, culture, value)!;
        }
    }


}
