using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal sealed class ChatContentArranger2(
    IChatsBackend chatsBackend,
    IDialogFragmentAnalyzer fragmentAnalyzer,
    IChatDialogFormatter chatDialogFormatter
) : IChatContentArranger
{
    public int MaxEntriesPerDocument { get; init; } = 12;
    public decimal DocumentSplitFactor { get; init; } = 0.75m;

    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (bufferedEntries.Count == 0)
            yield break;

        var builders = new List<DocumentBuilder>();
        // Preload document tails
        foreach (var tailDocument in tailDocuments) {
            var builder = new DocumentBuilder(tailDocument);
            var tailEntries = await LoadTailEntries(tailDocument, cancellationToken)
                .ConfigureAwait(false);
            builder.Entries.AddRange(tailEntries);
            builder.Dialog = await chatDialogFormatter.BuildUpDialog(tailEntries).ConfigureAwait(false);
        }

        foreach (var entry in bufferedEntries) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;
            if (entry.IsSystemEntry)
                continue;

            if (builders.Count == 0) {
                var builder = new DocumentBuilder(null) {
                    HasModified = true
                };
                builders.Add(builder);
                builder.Entries.Add(entry);
                builder.Dialog = await chatDialogFormatter.BuildUpDialog(builder.Entries).ConfigureAwait(false);
            }
            else {
                DocumentBuilder? builderToAdd = null;
                var candidates = new List<DocumentBuilder>();

                if (entry.RepliedEntryLid is { } repliedEntryLocalId) {
                    foreach (var builder in builders) {
                        if (builder.Entries.Any(c => c.LocalId == repliedEntryLocalId)) {
                            builderToAdd = builder;
                            break;
                        }
                    }
                }

                if (builderToAdd is null) {
                    List<DocumentBuilder>? buildersToClose = null;
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
                            buildersToClose ??= new List<DocumentBuilder>();
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
                    var builder = new DocumentBuilder(null) {
                        HasModified = true
                    };
                    builder.Entries.Add(entry);
                    builder.Dialog = await chatDialogFormatter.BuildUpDialog(builder.Entries).ConfigureAwait(false);
                    builders.Add(builder);
                }
            }

            List<DocumentBuilder>? buildersToSplit = null;
            foreach (var builder in builders) {
                if (builder.HasModified && builder.Entries.Count > MaxEntriesPerDocument) {
                    buildersToSplit ??= new List<DocumentBuilder>();
                    buildersToSplit.Add(builder);
                }
            }

            if (buildersToSplit is not null) {
                var splitDocumentEntriesNumber = (int)Math.Floor(MaxEntriesPerDocument * DocumentSplitFactor);
                foreach (var builder in buildersToSplit) {
                    var entriesHead = builder.Entries.Take(splitDocumentEntriesNumber).ToArray();
                    var entriesTail = builder.Entries.Skip(splitDocumentEntriesNumber).ToArray();
                    yield return new SourceEntries(null, null, entriesHead);

                    var tailBuilder = new DocumentBuilder(null) {
                        HasModified = true,
                    };

                    tailBuilder.Entries.AddRange(entriesTail);
                    tailBuilder.Dialog = await chatDialogFormatter.BuildUpDialog(tailBuilder.Entries).ConfigureAwait(false);
                    var index = builders.IndexOf(builder);
                    builders.Remove(builder);
                    builders.Insert(index, tailBuilder);
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

    private class DocumentBuilder(ChatSlice? relatedChatSlice)
    {
        public ChatSlice? RelatedChatSlice { get; } = relatedChatSlice;
        public bool HasModified { get; set; }
        public List<ChatEntry> Entries { get; } = [];
        public string Dialog { get; set; } = "";
        public string PossibleDialog { get; set; } = "";
    }
}
