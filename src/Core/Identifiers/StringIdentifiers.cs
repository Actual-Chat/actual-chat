using System;
using System.Runtime.Serialization;
// ReSharper disable PartialTypeWithSinglePart
#pragma warning disable 618

namespace ActualChat
{
    [DataContract]
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

    [DataContract]
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

    [DataContract]
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

    [DataContract]
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

    [DataContract]
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

}
