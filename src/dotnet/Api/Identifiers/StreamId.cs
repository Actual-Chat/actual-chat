using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a live audio stream.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<StreamId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<StreamId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<StreamId>))]
[TypeConverter(typeof(StringLikeTypeConverter<StreamId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class StreamId : StringIdentifier, IStringIdentifier<StreamId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<StreamId>();
    private static readonly ILruCache<string, StreamId> Cache = CreateCache<StreamId>(32);

    private static Func<string> LocalIdGenerator { get; } = () => Ulid.NewUlid().ToString();
    private const char Delimiter = '-';
    private const char LanguageDelimiter = '~';

    [IgnoreDataMember]
    public NodeRef NodeRef { get; }
    [IgnoreDataMember]
    public string LocalId { get; }
    [IgnoreDataMember]
    public Language? Language { get; }

    // Factories and constructors

    public static StreamId New(NodeRef nodeRef)
    {
        var localId = LocalIdGenerator.Invoke();
        return new(Format(nodeRef, localId), nodeRef, localId, null);
    }

    public static StreamId New(NodeRef nodeRef, string localId)
        => new(Format(nodeRef, localId), nodeRef, localId, null);

    public static StreamId New(StreamId streamId, Language language)
        => new(Format(streamId, language), streamId.NodeRef, streamId.LocalId, language);

    private StreamId(string value, NodeRef nodeRef, string localId, Language? language) : base(value)
    {
        NodeRef = nodeRef;
        LocalId = localId;
        Language = language;
    }

    // Equality

    public bool Equals(StreamId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is StreamId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(StreamId? left, StreamId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(StreamId? left, StreamId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(NodeRef nodeRef, string localId)
        => $"{nodeRef}{Delimiter}{localId}";

    public static string Format(StreamId streamId, Language language)
        => $"{streamId.Value}{LanguageDelimiter}{language}";

    public static StreamId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<StreamId>(s);

    public static StreamId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static StreamId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out StreamId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var languageParts = s.Split(LanguageDelimiter, 2);
        if (languageParts.Length == 2) {
            // StreamId with language
            if (!TryParse(languageParts[0], out var streamId))
                return false;

            if (!Language.TryParse(languageParts[1], out var language))
                return false;

            result = new StreamId(s, streamId.NodeRef, streamId.LocalId, language);
            result = Cache.AddOrGet(s, result);
            return true;
        }

        var parts = s.Split(Delimiter, 2);
        if (parts.Length != 2)
            return false;

        if (!NodeRef.TryParse(parts[0], out var nodeRef))
            return false;

        var localId = parts[1];
        if (localId.IsNullOrEmpty() || !Alphabet.AlphaNumericDash.IsMatch(localId))
            return false;

        result = new StreamId(s, nodeRef, localId, null);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
