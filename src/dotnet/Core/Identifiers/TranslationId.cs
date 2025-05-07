using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<TranslationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<TranslationId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<TranslationId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<TranslationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TranslationId : StringIdentifier, IStringIdentifier<TranslationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<TranslationId>();
    private static readonly ILruCache<string, TranslationId> Cache = CreateCache<TranslationId>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public TextEntryId ChatEntryId { get; }
    [IgnoreDataMember]
    public Language Language { get; }

    // Factories and constructors

    public static TranslationId New(TextEntryId chatEntryId, Language language)
        => new(Format(chatEntryId, language), chatEntryId, language);

    private TranslationId(string value, TextEntryId chatEntryId, Language language) : base(value)
    {
        ChatEntryId = chatEntryId;
        Language = language;
    }

    // Equality

    public bool Equals(TranslationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is TranslationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TranslationId? left, TranslationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TranslationId? left, TranslationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(TextEntryId chatEntryId, Language language)
        => $"{chatEntryId.Value}{Delimiter}{language.Value}";

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

        var entryIdLength = s.LastIndexOf(Delimiter);
        if (entryIdLength < 0)
            return false;

        if (!TextEntryId.TryParse(s[..entryIdLength], out var entryId))
            return false;

        var languageStart = entryIdLength + 1;
        if (!Language.TryParse(s[languageStart..], out var language))
            return false;

        result = new TranslationId(s, entryId, language);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
