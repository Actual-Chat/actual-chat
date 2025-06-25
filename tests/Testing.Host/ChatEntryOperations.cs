using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class ChatEntryOperations
{
    public static Task<ChatEntry> CreateTextEntry(
        this IWebTester tester,
        ChatId chatId,
        string text,
        MediaId? mediaId = null)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, chatId, null, text) {
            EntryAttachments = mediaId == null
                ? []
                : [
                    new TextEntryAttachment {
                        MediaId = mediaId,
                        Index = 0,
                    },
                ],
        });

    public static Task<ChatEntry> UpdateTextEntry(this IWebTester tester, ChatEntryId id, string text)
        => tester.Commander.Call(new Chats_UpsertTextEntry(tester.Session, id.ChatId, id.LocalId, text));

    public static Task RemoveTextEntry(this IWebTester tester, ChatEntryId id)
        => tester.Commander.Call(new Chats_RemoveTextEntry(tester.Session, id.ChatId, id.LocalId));

    public static async Task<ChatEntry[]> CreateTextEntries(this IWebTester tester, ChatId chatId, string textPrefix, int entryCount)
    {
        var entries = await Enumerable.Range(1, entryCount)
            .Select(async i => await tester.CreateTextEntry(chatId, $"{textPrefix} {i}").ConfigureAwait(false))
            .Collect(1);
        return entries;
    }

    public static async Task<StreamingEntry> CreateStreamingEntry(this IWebTester tester, ChatId chatId, Language language, CancellationToken cancellationToken = default)
    {
        var clocks = tester.AppServices.Clocks();
        var author = await tester.GetOwnAuthor(chatId, cancellationToken).Require();
        var now = clocks.SystemClock.Now;
        var audioEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(
                AudioEntryId.New(chatId, 0),
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    Content = "",
                    StreamId = StreamId.New(NodeRef.ThisNodeAlias).Value,
                    BeginsAt = now,
                    ClientSideBeginsAt = now,
                })),
            cancellationToken);
        var textEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(TextEntryId.New(chatId, 0),
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    StreamId = StreamId.New(NodeRef.ThisNodeAlias).Value,
                    AudioEntryLid = audioEntry.LocalId,
                    Content = "",
                })),
            cancellationToken: cancellationToken);
        var entryLanguage = await tester
            .CreateEntryLanguage(textEntry.Id.ToTextEntryId(), language, textEntry.ContentHash, cancellationToken)
            .ConfigureAwait(false);
        return new (audioEntry, textEntry, entryLanguage);
    }

    public static async Task<StreamingEntry> FinalizeStreamingEntry(
        this IWebTester tester,
        StreamingEntry streamingEntry,
        string text,
        CancellationToken cancellationToken = default)
    {
        var clocks = tester.AppServices.Clocks();
        var now = clocks.SystemClock.Now;
        var (audioEntry, textEntry, _) = streamingEntry;
        audioEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(
            audioEntry.Id,
            null, // do not perform version check there - it might have already been changed and it's OK
            Change.Update(new ChatEntryDiff {
                Content = "fake-blob-id",
                StreamId = "",
                EndsAt = now,
                ContentEndsAt = now,
            })), cancellationToken);

        textEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(textEntry.Id, textEntry.Version, Change.Update(new ChatEntryDiff {
            Content = text,
            StreamId = "",
            AudioEntryLid = audioEntry.LocalId,
            EndsAt = now,
            TimeMap = LinearMap.Zero,
        })), cancellationToken);

        var entryLanguage = await tester.UpdateEntryLanguage(
            streamingEntry.EntryLanguage with { EntryContentHash = textEntry.ContentHash },
            cancellationToken);
        return new (audioEntry, textEntry, entryLanguage);
    }
}

public sealed record StreamingEntry(ChatEntry AudioEntry, ChatEntry TextEntry, ChatEntryLanguage EntryLanguage);
