using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

// Identifies a transcriber *configuration* rather than a vendor, so configurations sharing a
// DriverId rank independently. Built-in keys are "{provider}-{mode}", with room for a model part
// ("google-gemini-offline") once a provider exposes more than one model per mode.

/// <summary>
/// Unique identifier of a transcriber configuration: a bare key for built-in ones,
/// <c>u:{key}</c> for user-supplied ones, and <c>{stream}~{retranscriber}</c> for a pair.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<TranscriberId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<TranscriberId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<TranscriberId>))]
[TypeConverter(typeof(StringLikeTypeConverter<TranscriberId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TranscriberId : StringIdentifier, IStringIdentifier<TranscriberId>
{
    public static readonly TranscriberId None = new("", TranscriberSource.Builtin, "");
    public static readonly TranscriberId GoogleStream = NewBuiltin("google-stream");
    public static readonly TranscriberId DeepgramStream = NewBuiltin("deepgram-stream");
    public static readonly TranscriberId OpenAIStream = NewBuiltin("openai-stream");
    public static readonly TranscriberId OpenAIOffline = NewBuiltin("openai-offline");
    public static readonly TranscriberId SonioxStream = NewBuiltin("soniox-stream");
    public static readonly TranscriberId SonioxOffline = NewBuiltin("soniox-offline");
    private const string UserPrefix = "u:";
    private const char PairSeparator = '~';
    private static readonly ILruCache<string, TranscriberId> Cache = CreateCache<TranscriberId>(64);

    [IgnoreDataMember]
    public TranscriberSource Source { get; }
    [IgnoreDataMember]
    public Symbol Key { get; }
    [IgnoreDataMember]
    public TranscriberId PrimaryId { get; }
    [IgnoreDataMember]
    public TranscriberId? RetranscriberId { get; }
    [IgnoreDataMember]
    public bool IsNone => Value.IsNullOrEmpty();
    [IgnoreDataMember]
    public bool IsPair => RetranscriberId != null;

    // Factories and constructors

    public static TranscriberId NewBuiltin(Symbol key)
        => new(Format(TranscriberSource.Builtin, key), TranscriberSource.Builtin, key);

    public static TranscriberId NewUser(Symbol key)
        => new(Format(TranscriberSource.User, key), TranscriberSource.User, key);

    public static TranscriberId NewPair(TranscriberId primary, TranscriberId retranscriber)
        => Parse(primary.Value + PairSeparator + retranscriber.Value);

    private TranscriberId(
        string value,
        TranscriberSource source,
        Symbol key,
        TranscriberId? primaryId = null,
        TranscriberId? retranscriberId = null)
        : base(value)
    {
        Source = source;
        Key = key;
        PrimaryId = primaryId ?? this;
        RetranscriberId = retranscriberId;
    }

    // Equality

    public bool Equals(TranscriberId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is TranscriberId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TranscriberId? left, TranscriberId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TranscriberId? left, TranscriberId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(TranscriberSource source, Symbol key)
        => source == TranscriberSource.User ? UserPrefix + key.Value : key.Value;

    public static TranscriberId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TranscriberId>(s);

    public static TranscriberId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static TranscriberId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TranscriberId? result)
    {
        result = null;
        if (s is null)
            return false;
        if (s.Length == 0) {
            result = None;
            return true;
        }
        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        // A pair is "{stream}~{retranscriber}"; both halves must be atomic, so nesting can't happen.
        var separatorIndex = s.IndexOf(PairSeparator);
        if (separatorIndex >= 0) {
            if (!TryParse(s[..separatorIndex], out var primary) || primary.IsNone || primary.IsPair)
                return false;
            if (!TryParse(s[(separatorIndex + 1)..], out var retranscriber)
                || retranscriber.IsNone || retranscriber.IsPair)
                return false;

            result = new TranscriberId(s, primary.Source, primary.Key, primary, retranscriber);
            result = Cache.AddOrGet(s, result);
            return true;
        }

        var isUser = s.StartsWith(UserPrefix);
        var key = isUser ? s[UserPrefix.Length..] : s;
        if (key.Length == 0)
            return false;

        var source = isUser ? TranscriberSource.User : TranscriberSource.Builtin;
        result = new TranscriberId(s, source, new Symbol(key));
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
