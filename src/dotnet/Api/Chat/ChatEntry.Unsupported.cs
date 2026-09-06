using System.Collections.Frozen;

namespace ActualChat.Chat;

public abstract partial record ChatEntry : IForwardCompatibleUnion<ChatEntry>
{
    public const int FirstSystemUnionTag = 100;
    public const int LastSystemUnionTag = 199;

    // Tags 0..2 predate the ranges below, so they're classified by name rather than by value.
    // They stay here even if their members are ever removed: a removed member's tag becomes
    // unknown to later builds while old data still carries it, and a pure range check would
    // then put a system entry on the message side.
    private static readonly FrozenSet<int> LegacySystemUnionTags = new[] { 1, 2 }.ToFrozenSet();

    public static bool IsSystemUnionTag(int tag)
        => LegacySystemUnionTags.Contains(tag)
            || tag is >= FirstSystemUnionTag and <= LastSystemUnionTag;

    static ChatEntry? IForwardCompatibleUnion<ChatEntry>.NewUnsupported(
        int tag, ref MessagePackReader payload, MessagePackSerializerOptions options)
        => NewUnsupported(IsSystemUnionTag(tag), ref payload, options);

    // Protected/internal methods

    internal static ChatEntry NewUnsupported(
        bool isSystemEntry, ref MessagePackReader payload, MessagePackSerializerOptions options)
    {
        var (id, version, flags, authorId) = ReadPrefix(ref payload, options);
        ChatEntry entry = isSystemEntry
            ? new UnsupportedSystemEntry(id, version)
            : new TextEntry(id, version);
        return entry with {
            Flags = flags | ChatEntryFlags.IsUnsupported,
            AuthorId = authorId,
        };
    }

    // Private methods

    private static (ChatEntryId Id, long Version, ChatEntryFlags Flags, AuthorId AuthorId) ReadPrefix(
        ref MessagePackReader payload, MessagePackSerializerOptions options)
    {
        var id = default(ChatEntryId)!;
        var version = 0L;
        var flags = ChatEntryFlags.None;
        var authorId = default(AuthorId)!;
        switch (payload.NextMessagePackType) {
        case MessagePackType.Array:
            // Keyed layout: every member's payload starts with the base record's keys, in order.
            var itemCount = payload.ReadArrayHeader();
            for (var i = 0; i < itemCount && i <= 3; i++)
                switch (i) {
                case 0:
                    id = MessagePackSerializer.Deserialize<ChatEntryId>(ref payload, options);
                    break;
                case 1:
                    version = payload.ReadInt64();
                    break;
                case 2:
                    flags = MessagePackSerializer.Deserialize<ChatEntryFlags>(ref payload, options);
                    break;
                case 3:
                    authorId = MessagePackSerializer.Deserialize<AuthorId>(ref payload, options);
                    break;
                }
            break;
        case MessagePackType.Map:
            // Keyless layout: derived members are written first, so there is no prefix to walk.
            var pairCount = payload.ReadMapHeader();
            for (var i = 0; i < pairCount; i++)
                switch (payload.ReadString()) {
                case nameof(Id):
                    id = MessagePackSerializer.Deserialize<ChatEntryId>(ref payload, options);
                    break;
                case nameof(Version):
                    version = payload.ReadInt64();
                    break;
                case nameof(Flags):
                    flags = MessagePackSerializer.Deserialize<ChatEntryFlags>(ref payload, options);
                    break;
                case nameof(AuthorId):
                    authorId = MessagePackSerializer.Deserialize<AuthorId>(ref payload, options);
                    break;
                default:
                    payload.Skip();
                    break;
                }
            break;
        }
        return (id, version, flags, authorId);
    }
}
