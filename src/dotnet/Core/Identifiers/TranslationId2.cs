using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<TranslationId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<TranslationId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<TranslationId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<TranslationId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TranslationId2 : StringIdentifier, IStringIdentifier<TranslationId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<TranslationId2>();
    private static readonly ILruCache<string, TranslationId2> Cache = CreateCache<TranslationId2>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public TextEntryId2 ChatEntryId { get; }
    [IgnoreDataMember]
    public Language Language { get; }

    // Factories and constructors

    public static TranslationId2 New(TextEntryId2 chatEntryId, Language language)
        => new(Format(chatEntryId, language), chatEntryId, language);

    private TranslationId2(string value, TextEntryId2 chatEntryId, Language language) : base(value)
    {
        ChatEntryId = chatEntryId;
        Language = language;
    }

    // Equality

    public bool Equals(TranslationId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is TranslationId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TranslationId2? left, TranslationId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TranslationId2? left, TranslationId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(TextEntryId2 chatEntryId, Language language)
        => $"{chatEntryId.Value}{Delimiter}{language.Value}";

    public static TranslationId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TranslationId2>(s);

    public static TranslationId2? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static TranslationId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TranslationId2? result)
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

        if (!TextEntryId2.TryParse(s[..entryIdLength], out var entryId))
            return false;

        var languageStart = entryIdLength + 1;
        if (!Language.TryParse(s[languageStart..], out var language))
            return false;

        result = new TranslationId2(s, entryId, language);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
