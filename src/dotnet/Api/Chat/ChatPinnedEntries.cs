using ActualChat.Kvas;

namespace ActualChat.Chat;

/// <summary>
/// Chat-scoped list of pinned message ids (Telegram-style), stored via chat-scoped KVAS.
/// Order in <see cref="EntryIds"/> is the display order; capped at <see cref="MaxCount"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatPinnedEntries : IHasKvasKey<ChatPinnedEntries>
{
    public const int MaxCount = 3;
    public static readonly ChatPinnedEntries None = new();
    public static string KvasKey => "@PinnedEntries";

    [DataMember, Key(0)] public ApiArray<ChatEntryId> EntryIds { get; init; }
}
