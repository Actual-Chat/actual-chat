using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Generators;

namespace ActualChat.UI.Blazor.App.Components;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatMessageKey>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatMessageKey>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatMessageKey>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ChatMessageKey : StringIdentifier, IStringIdentifier<ChatMessageKey>
{
    private static readonly Dictionary<ChatMessageKind, string> SuffixByKind = new () {
        [ChatMessageKind.DateLine] = "-date-line",
        [ChatMessageKind.NewMessagesLine] = "-new-messages",
        [ChatMessageKind.WelcomeBlock] = "-welcome-block",
        [ChatMessageKind.Group] = "-group",
        [ChatMessageKind.SearchWelcomeBlock] = "-search-welcome-block",
        [ChatMessageKind.ConversationBlock] = "-conversation-block",
        [ChatMessageKind.ConversationStart] = "-conversation",
        [ChatMessageKind.ConversationEnd] = "-conversation-end",
        [ChatMessageKind.Thread] = "-thread",
        [ChatMessageKind.None] = "",
    };

    private static readonly Dictionary<string, ChatMessageKind> KindBySuffix =
        SuffixByKind.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

    public const char Delimiter = '-';

    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatMessageKey>();
    private static readonly ILruCache<string, ChatMessageKey> Cache = CreateCache<ChatMessageKey>(512);

    public static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    [IgnoreDataMember]
    public ChatMessageKind Kind { get; }

    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    protected ChatMessageKey(string value, ChatMessageKind kind, long localId) : base(value)
    {
        Kind = kind;
        LocalId = localId;
    }

    public static ChatMessageKey New(ChatMessageKind kind, long localId)
        => new(Format(kind, localId), kind, localId);

    // Equality

    public bool Equals(ChatMessageKey? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ChatMessageKey other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatMessageKey? left, ChatMessageKey? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatMessageKey? left, ChatMessageKey? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatMessageKind kind, long localId)
        => $"{localId}{SuffixByKind[kind]}";

    public static ChatMessageKey Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatMessageKey>(s);

    public static ChatMessageKey? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ChatMessageKey? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatMessageKey? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var firstDashIndex = s.OrdinalIndexOf('-');
        var sLocalId = firstDashIndex > 0 ? s[..firstDashIndex] : s;
        if (!long.TryParse(sLocalId, CultureInfo.InvariantCulture, out var localId))
            return false;

        var kind = ChatMessageKind.None;
        if (firstDashIndex >= 0)
            if (!KindBySuffix.TryGetValue(s[firstDashIndex..], out kind))
                return false;

        result = new ChatMessageKey(s, kind, localId);

        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Private methods
}
