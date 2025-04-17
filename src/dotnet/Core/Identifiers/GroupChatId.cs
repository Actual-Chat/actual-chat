using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<GroupChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<GroupChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<GroupChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct GroupChatId : ISymbolIdentifier<GroupChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<GroupChatId>();
    internal static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public static GroupChatId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Parsed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long ThreadId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsThread => ThreadId > 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId Parent => !IsThread ? this : new GroupChatId(ChatId, ChatId, 0, AssumeValid.Option);

    internal static GroupChatId Group(string chatId)
        => new (chatId, chatId, 0, AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public GroupChatId(Symbol id) => this = Parse(id);
    public GroupChatId(string? id) => this = Parse(id);
    public GroupChatId(string? id, ParseOrNone _) => ParseOrNone(id);

    public GroupChatId(Generate _)
        => this = new GroupChatId(IdGenerator.Next());

    private GroupChatId(Symbol id, Symbol chatId, long threadId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        ThreadId = threadId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(GroupChatId source) => source.Id;
    public static implicit operator string(GroupChatId source) => source.Id.Value;

    // Equality

    public bool Equals(GroupChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is GroupChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(GroupChatId left, GroupChatId right) => left.Equals(right);
    public static bool operator !=(GroupChatId left, GroupChatId right) => !left.Equals(right);

    // Parsing

    public static GroupChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<GroupChatId>(s);
    public static GroupChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<GroupChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out GroupChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6)
            return false;

        var sRawChatId = s;
        var rawChatIdLength = s.LastIndexOf(ActualChat.ChatId.ThreadIdSeparator);
        long threadId = 0;
        if (rawChatIdLength > -1) {
            var sThreadId = s.AsSpan().Slice(rawChatIdLength + 1);
            if (long.TryParse(sThreadId, CultureInfo.InvariantCulture, out threadId))
                sRawChatId = s.Substring(0, rawChatIdLength);
        }

        if (sRawChatId.Length < 6)
            return false;

        if (!(Alphabet.AlphaNumeric.IsMatch(sRawChatId) || Constants.Chat.SystemChatIds.Contains(sRawChatId)))
            return false;

        // Group chat ID
        result = new GroupChatId(s, sRawChatId, threadId, AssumeValid.Option);
        return true;
    }
}
