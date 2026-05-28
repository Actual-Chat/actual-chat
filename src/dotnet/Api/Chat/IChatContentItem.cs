using ActualLab.Versioning;

namespace ActualChat.Chat;

// Common shape of indexed chat content items. Implementations:
// VisualMediaItem, FileItem, LinkItem — one per content kind / storage table.
public interface IChatContentItem : IHasId<Symbol>, IHasVersion<long>
{
    ChatEntryId EntryId { get; }
    int LocalIndex { get; }
    Moment At { get; }

    ChatId ChatId => EntryId.ChatId;
    long EntryLocalId => EntryId.LocalId;
}
