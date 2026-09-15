using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat.UI.Blazor.App.Components;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<ChatMessageKey>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ChatMessageKey>))]
[TypeConverter(typeof(StringLikeTypeConverter<ChatMessageKey>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ChatMessageKey : StringIdentifier, IStringIdentifier<ChatMessageKey>
{
    private static readonly Dictionary<ChatMessageKind, string> SuffixByKind = new () {
        [ChatMessageKind.DateLine] = "-date-line",
        [ChatMessageKind.NewMessagesLine] = "-new-messages",
        [ChatMessageKind.WelcomeBlock] = "-welcome-block",
        [ChatMessageKind.Group] = "-group",
        [ChatMessageKind.ConversationBlock] = "-conversation-block",
        [ChatMessageKind.ConversationStart] = "-conversation",
        [ChatMessageKind.ConversationEnd] = "-conversation-end",
        [ChatMessageKind.LiveConversationHeader] = "-live-header",
        [ChatMessageKind.Thread] = "-thread",
        [ChatMessageKind.SendingNewMessage] = "-sending-new-msg",
        [ChatMessageKind.AudioRecordingMessage] = "-audio-recording-msg",
        [ChatMessageKind.None] = "",
    };

    private static readonly Dictionary<string, ChatMessageKind> KindBySuffix =
        SuffixByKind.ToDictionary(x => x.Value, x => x.Key);

    public const char Delimiter = '-';

    private static readonly ILruCache<string, ChatMessageKey> Cache = CreateCache<ChatMessageKey>(512);

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
            && string.Equals(Value, other.Value);
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

        var firstDashIndex = s.IndexOf('-');
        var sLocalId = firstDashIndex > 0 ? s[..firstDashIndex] : s;
        if (!long.TryParse(sLocalId, out var localId))
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
