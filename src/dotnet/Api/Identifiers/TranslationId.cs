using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a translation of content into a specific language.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<TranslationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<TranslationId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<TranslationId>))]
[TypeConverter(typeof(StringLikeTypeConverter<TranslationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TranslationId : StringIdentifier, IStringIdentifier<TranslationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<TranslationId>();
    private static readonly ILruCache<string, TranslationId> Cache = CreateCache<TranslationId>(256);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public TranslationSourceId SourceId { get; }
    [IgnoreDataMember]
    public Language Language { get; }

    public TranslationIdKind Kind => SourceId.Kind;

    // Factories and constructors

    public static TranslationId New(ChatEntryId chatEntryId, Language language)
        => New(TranslationSourceId.New(chatEntryId), language);

    public static TranslationId New(TranslationSourceId translationSourceId, Language language)
        => Parse(Format(translationSourceId, language));

    private TranslationId(string value, TranslationSourceId sourceId, Language language) : base(value)
    {
        SourceId = sourceId;
        Language = language;
    }

    // Equality

    public bool Equals(TranslationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is TranslationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TranslationId? left, TranslationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TranslationId? left, TranslationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(TranslationSourceId sourceId, Language language)
        => $"{sourceId.Value}{Delimiter}{language.Value}";

    public static TranslationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TranslationId>(s);

    public static TranslationId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static TranslationId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TranslationId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var sourceIdLength = s.LastIndexOf(Delimiter);
        if (sourceIdLength < 0)
            return false;

        var languageStart = sourceIdLength + 1;
        if (!Language.TryParse(s[languageStart..], out var language))
            return false;

        if (!TranslationSourceId.TryParse(s.Substring(0, sourceIdLength), out var sourceId))
            return false;

        result = new TranslationId(s, sourceId, language);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
