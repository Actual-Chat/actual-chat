using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal sealed class ChatContentArranger2(
    IChatsBackend chatsBackend,
    IDialogFragmentAnalyzer fragmentAnalyzer,
    ChatDialogFormatter chatDialogFormatter
) : IChatContentArranger
{
    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (bufferedEntries.Count == 0)
            yield break;

        // TODO: we want to select document depending on the content
        var builders = new List<SourceEntriesBuilder>();
        foreach (var tailDocument in tailDocuments)
            builders.Add(new SourceEntriesBuilder(tailDocument));

        // Preload document tails
        foreach (var builder in builders) {
            if (builder is { RelatedChatSlice: not null, HasInitializedEntries: false }) {
                var tailEntries = await LoadTailEntries(builder.RelatedChatSlice, cancellationToken)
                    .ConfigureAwait(false);
                builder.Entries.AddRange(tailEntries);
                builder.HasInitializedEntries = true;
                builder.Dialog = await chatDialogFormatter.BuildUpDialog(tailEntries).ConfigureAwait(false);
            }
        }

        foreach (var entry in bufferedEntries) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;
            if (entry.IsSystemEntry)
                continue;

            if (builders.Count == 0) {
                var builder = new SourceEntriesBuilder(null) {
                    HasInitializedEntries = true,
                    HasModified = true
                };
                builders.Add(builder);
                builder.Entries.Add(entry);
                builder.Dialog = await chatDialogFormatter.BuildUpDialog(builder.Entries).ConfigureAwait(false);
            }
            else {
                SourceEntriesBuilder? builderToAdd = null;
                var candidates = new List<SourceEntriesBuilder>();

                if (entry.RepliedEntryLid is { } repliedEntryLocalId) {
                    foreach (var builder in builders) {
                        if (builder.Entries.Any(c => c.LocalId == repliedEntryLocalId)) {
                            builderToAdd = builder;
                            break;
                        }
                    }
                }

                if (builderToAdd is null) {
                    List<SourceEntriesBuilder>? buildersToClose = null;
                    foreach (var builder in builders) {
                        var lastEntry = builder.Entries.Last();
                        var timeDistance = entry.BeginsAt - lastEntry.BeginsAt;
                        var idDistance = entry.LocalId - lastEntry.LocalId;
                        var shouldCloseBuilder = idDistance > 100
                            || (idDistance > 75 && timeDistance > TimeSpan.FromMinutes(30))
                            || (idDistance > 50 && timeDistance > TimeSpan.FromMinutes(60))
                            || (idDistance > 30 && timeDistance > TimeSpan.FromMinutes(120))
                            || (idDistance > 10 && timeDistance > TimeSpan.FromMinutes(240))
                            || timeDistance > TimeSpan.FromHours(12);
                        if (shouldCloseBuilder) {
                            buildersToClose ??= new List<SourceEntriesBuilder>();
                            buildersToClose.Add(builder);
                        }
                    }

                    if (buildersToClose != null) {
                        foreach (var builder in buildersToClose) {
                            builders.Remove(builder);
                            if (builder.HasModified)
                                yield return new SourceEntries(null, null, builder.Entries);
                        }
                    }

                    foreach (var builder in builders) {
                        var dialog = builder.Dialog;
                        var entryText = await chatDialogFormatter.EntryToText(entry, builder.Entries[^1])
                            .ConfigureAwait(false);
                        dialog = dialog + Environment.NewLine + entryText;
                        builder.PossibleDialog = dialog;
                        var result = await fragmentAnalyzer.IsDialogAboutTheSameTopic(dialog).ConfigureAwait(false);
                        if (result is { HasValue: true, Value: true })
                            candidates.Add(builder);
                    }
                }
                if (candidates.Count == 1)
                    builderToAdd = candidates[0];
                else if (candidates.Count > 1) {
                    var dialogs = candidates.Select(c => c.PossibleDialog).ToArray();
                    var index = await fragmentAnalyzer.ChooseMoreProbableDialog(dialogs).ConfigureAwait(false);
                    if (index >= 0)
                        builderToAdd = candidates[index];
                }

                if (builderToAdd != null) {
                    builderToAdd.HasModified = true;
                    builderToAdd.Entries.Add(entry);
                    builderToAdd.Dialog = builderToAdd.PossibleDialog;
                }
                else {
                    var builder = new SourceEntriesBuilder(null) {
                        HasInitializedEntries = true,
                        HasModified = true
                    };
                    builder.Entries.Add(entry);
                    builder.Dialog = await chatDialogFormatter.BuildUpDialog(builder.Entries).ConfigureAwait(false);
                    builders.Add(builder);
                }
            }
        }

        foreach (var builder in builders) {
            if (builder.HasModified)
                yield return new SourceEntries(null, null, builder.Entries);
        }
    }

    private async ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(
        ChatSlice tailDocument, CancellationToken cancellationToken)
    {
        var tailEntryIds = tailDocument.Metadata
            .ChatEntries
            .Select(e => e.Id);
        return await chatsBackend.GetEntries(tailEntryIds, false, cancellationToken).ConfigureAwait(false);
    }

    private record SourceEntriesBuilder(ChatSlice? RelatedChatSlice)
    {
        public bool HasInitializedEntries { get; set; }
        public bool HasModified { get; set; }
        public List<ChatEntry> Entries { get; } = [];
        public string Dialog { get; set; } = "";
        public string PossibleDialog { get; set; } = "";
    }
}
