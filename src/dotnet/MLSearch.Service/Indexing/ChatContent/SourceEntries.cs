using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal record SourceEntries(int StartOffset, int? EndOffset, IReadOnlyList<ChatEntry> Entries);
