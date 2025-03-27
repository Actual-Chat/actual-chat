using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ChatId : ISymbolIdentifier<ChatId>
{
    public const char ThreadIdSeparator = '-';

    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatId>();

    public static ChatId None { get; } = new ChatId(AssumeValid.Option);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PeerChatId PeerChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceChatId PlaceChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public GroupChatId GroupChatId { get => field ?? GroupChatId.None; } = GroupChatId.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatKind Kind => !PlaceChatId.IsNone
        ? ChatKind.Place
        : PeerChatId.IsNone
            ? ChatKind.Group
            : ChatKind.Peer;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsPlaceChat => !PlaceChatId.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsPlaceRootChat => IsPlaceChat && PlaceChatId.IsRoot;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceId PlaceId => PlaceChatId.PlaceId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long ThreadId => Kind switch {
        ChatKind.Group => GroupChatId.ThreadId,
        ChatKind.Place => PlaceChatId.ThreadId,
        _ => 0,
    };
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsThread => ThreadId > 0;
    public ChatId GetThreadParentOrSelf() => Kind switch {
            ChatKind.Group => GroupChatId.GetThreadParentOrSelf(),
            ChatKind.Place => PlaceChatId.GetThreadParentOrSelf(),
            _ => this,
        };
    public ChatId GetThreadParent()
    {
        if (!IsThread)
            throw StandardError.NotSupported("This method is supported only for thread chat ids.");
        return Kind switch {
            ChatKind.Group => GroupChatId.GetThreadParentOrSelf(),
            ChatKind.Place => PlaceChatId.GetThreadParentOrSelf(),
            _ => this,
        };
    }

    public ChatId GetOutermostThreadParentOrSelf() {
        var result = this;
        while (result.IsThread)
            result = result.GetThreadParentOrSelf();
        return result;
    }

    // Factories

    public static ChatId Group(GroupChatId groupChatId)
        => new(groupChatId.Id, groupChatId, default, default, AssumeValid.Option);
    public static ChatId Peer(PeerChatId peerChatId)
        => new(peerChatId.Id, GroupChatId.None, peerChatId, default,  AssumeValid.Option);
    public static ChatId Place(PlaceChatId placeChatId)
        => new(placeChatId.Id, GroupChatId.None, default, placeChatId, AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ChatId(Symbol id)
        => this = Parse(id);
    public ChatId(string? id)
        => this = Parse(id);
    public ChatId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public ChatId(Generate _)
        => this = new GroupChatId(Generate.Option);

    private ChatId(Symbol id, GroupChatId groupChatId, PeerChatId peerChatId, PlaceChatId placeChatId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        GroupChatId = groupChatId;
        PeerChatId = peerChatId;
        PlaceChatId = placeChatId;
    }

    private ChatId(AssumeValid _)
    {
        // NOTE(DF): This constructor should be used to create None instance only.
        Id = "";
        GroupChatId = GroupChatId.None;
        PeerChatId = PeerChatId.None;
        PlaceChatId = PlaceChatId.None;
    }

    // Helpers

    public bool IsPeerChat(out PeerChatId peerChatId)
    {
        peerChatId = PeerChatId;
        return !peerChatId.IsNone;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatId source) => source.Id;
    public static implicit operator string(ChatId source) => source.Id.Value;
    public static implicit operator ChatId(PeerChatId source) => new(source.Id, GroupChatId.None, source, PlaceChatId.None, AssumeValid.Option);
    public static implicit operator ChatId(PlaceChatId source) => new(source.Id, GroupChatId.None, PeerChatId.None, source, AssumeValid.Option);
    public static implicit operator ChatId(GroupChatId source) => new(source.Id, source, PeerChatId.None, PlaceChatId.None, AssumeValid.Option);
    public static explicit operator ChatId(string source) => new(source);

    // Equality

    public bool Equals(ChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
    public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);

    // Parsing

    public static ChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>(s);
    public static ChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6)
            return false;

        if (s.OrdinalStartsWith(PeerChatId.IdPrefix)) {
            // Peer chat ID
            if (!PeerChatId.TryParse(s, out var peerChatId))
                return false;

            result = peerChatId;
        }
        else if (s.OrdinalStartsWith(PlaceChatId.IdPrefix)) {
            // Place chat ID
            if (!PlaceChatId.TryParse(s, out var placeChatId))
                return false;

            result = placeChatId;
        }
        else {
            // Group chat ID
            if (!GroupChatId.TryParse(s, out var groupChatId))
                return false;

            result = groupChatId;
        }
        return true;
    }

    public ChatId CreateThreadId(long threadId)
    {
        if (Kind is not (ChatKind.Group or ChatKind.Place))
            throw StandardError.NotSupported($"{Kind} chats do not support threads");
        return Parse(Value + ThreadIdSeparator + threadId.ToInvariantString());
    }

    public void EnsureNonThread()
    {
        if (IsThread)
            throw StandardError.Constraint("ChatId should not belong to Thread");
    }
}
