
using System.Text;
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal class ChatSliceMapper(
    IMarkupParser markupParser,
    IReactionsBackend reactionsBackend
) : IDocumentMapper<SourceEntries, IReadOnlyCollection<ChatSlice>>
{
    public async ValueTask<IReadOnlyCollection<ChatSlice>> MapAsync(SourceEntries sourceEntries, CancellationToken cancellationToken = default)
    {
        // TODO: in the future we may want to split sourse sequence into several documents
        // but for now lets create just single ChatSlice

        var entryCount = sourceEntries.Entries.Count;
        var principalSet = new HashSet<PrincipalId>(entryCount);

        // -- Authors
        principalSet.AddRange(sourceEntries.Entries.Select(e => new PrincipalId(e.AuthorId.Id)));
        var authors = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        authors.AddRange(principalSet);

        // -- Chat Entries
        var chatEntries = ImmutableArray.CreateBuilder<ChatSliceEntry>(entryCount);
        chatEntries.AddRange(sourceEntries.Entries.Select(e => new ChatSliceEntry(e.Id, e.LocalId, e.Version)));

        // -- Replies
        const int replyToEstimatedCount = 1;
        var uniqueReplyToEnries = new HashSet<ChatEntryId>(replyToEstimatedCount);
        uniqueReplyToEnries.AddRange(sourceEntries.Entries
            .Where(e => e.RepliedEntryLocalId is not null)
            .Select(e => new ChatEntryId(e.ChatId, ChatEntryKind.Text, e.RepliedEntryLocalId!.Value, AssumeValid.Option)));
        // TODO: We may want to build some summary for the entries we are replying to
        // We may use that summary while building document content later
        var replyToEntries = ImmutableArray.CreateBuilder<ChatEntryId>(uniqueReplyToEnries.Count);
        replyToEntries.AddRange(uniqueReplyToEnries);

        // -- Mentions
        var mentionExtractor = new MentionExtractor();
        principalSet.Clear();
        principalSet.AddRange(sourceEntries.Entries
            .Where(e => e.HasMarkup)
            .SelectMany(e => mentionExtractor.GetMentionIds(markupParser.Parse(e.Content)))
            .Select(mentionId => mentionId.PrincipalId));
        var mentions = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        mentions.AddRange(principalSet);

        // -- Reactions
        principalSet.Clear();
        foreach (var entryId in sourceEntries.Entries.Where(e => e.HasReactions).Select(e => e.Id.ToTextEntryId())) {
            var reactionSummary = await reactionsBackend.List(entryId, cancellationToken).ConfigureAwait(false);
            principalSet.AddRange(reactionSummary.SelectMany(s => s.FirstAuthorIds).Select(author => new PrincipalId(author.Id)));
        }
        var reactions = ImmutableArray.CreateBuilder<PrincipalId>(principalSet.Count);
        reactions.AddRange(principalSet);

        // -- Attachments
        var uniqueAttachments = new HashSet<MediaId>(entryCount);
        uniqueAttachments.AddRange(sourceEntries.Entries
            .SelectMany(e => e.Attachments)
            .Select(a => a.MediaId));
        var attachments = ImmutableArray.CreateBuilder<ChatSliceAttachment>(uniqueAttachments.Count);
        attachments.AddRange(uniqueAttachments.Select(mediaId => {
            // TODO: we may want to build summary with help of ML
            const string summary = "No summary yet";
            return new ChatSliceAttachment(mediaId, summary);
        }));

        // -- Timestamp
        var timestamp = sourceEntries.Entries.Select(e => e.BeginsAt).First();

        // -- Content
        var content = sourceEntries.Entries
            .Select((e, i) => {
                var content = e.Content;
                var (isFirst, isLast) = (i == 0, i == entryCount - 1);
                if (isFirst || isLast) {
                    var start = isFirst ? sourceEntries.StartOffset : 0;
                    var end = (isLast ? sourceEntries.EndOffset : null) ?? content.Length;
                    return content.Substring(start, end - start);
                }
                return content;
            })
            .Aggregate(new StringBuilder(), (sb, line) => sb.AppendLine(line));

        var metadata = new ChatSliceMetadata(
            Authors: authors.MoveToImmutable(),
            ChatEntries: chatEntries.MoveToImmutable(),
            StartOffset: sourceEntries.StartOffset,
            EndOffset: sourceEntries.EndOffset,
            ReplyToEntries: replyToEntries.MoveToImmutable(),
            Mentions: mentions.MoveToImmutable(),
            // TODO: talk: seems it's a bit too much.
            Reactions: reactions.MoveToImmutable(),
            Attachments: attachments.MoveToImmutable(),
            // TODO:
            IsPublic: true,
            Language: null,
            // TODO:
            Timestamp: timestamp
        );
        return [new ChatSlice(metadata, content.ToString())];
    }
}
