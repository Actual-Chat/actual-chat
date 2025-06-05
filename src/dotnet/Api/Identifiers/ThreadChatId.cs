using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ThreadChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ThreadChatId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ThreadChatId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ThreadChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ThreadChatId : ChatId, IStringIdentifier<ThreadChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ThreadChatId>();

    public ChatId ParentChatId { get; }
    public long ThreadId { get; }

    // Factories and constructors

    internal ThreadChatId(ChatId parentChatId, long threadId) : base(GetThreadChatIdValue(parentChatId, threadId), parentChatId.Kind)
    {
        ParentChatId = parentChatId;
        ThreadId = threadId;
    }

    private static string GetThreadChatIdValue(ChatId parentChatId, long threadId)
        => parentChatId.Value + ThreadIdSeparator + threadId.ToInvariantString();

    // Equality

    public bool Equals(ThreadChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ThreadChatId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ThreadChatId? left, ThreadChatId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ThreadChatId? left, ThreadChatId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new ThreadChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ThreadChatId>(s);

    public static new ThreadChatId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new ThreadChatId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ThreadChatId? result)
    {
        if (!ChatId.TryParse(s, out var chatId)) {
            result = null;
            return false;
        }

        result = chatId as ThreadChatId;
        return result is not null;
    }

    public ChatId GetOutermostParent() {
        ChatId result = this;
        while (result.IsThread(out var threadChatId))
            result = threadChatId.ParentChatId;
        return result;
    }
}
