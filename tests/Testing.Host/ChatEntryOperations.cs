
namespace ActualChat.Testing.Host;

public static class ChatEntryOperations
{
    public static Task<ChatEntry> CreateTextEntry(
        this IWebTester tester,
        ChatId chatId,
        string text,
        MediaId? mediaId = null)
    {
        var cmd = new Chats_UpsertEntry {
            Session = tester.Session,
            ChatId = chatId,
            LocalId = null,
            Text = text,
            Attachments = mediaId == null
            ? []
            : [
                new ChatEntryAttachment {
                    MediaId = mediaId,
                    Index = 0,
                },
            ],
        };
        return tester.Commander.Call(cmd);
    }

    public static Task<ChatEntry> UpdateTextEntry(this IWebTester tester, ChatEntryId id, string text)
    {
        var cmd = new Chats_UpsertEntry {
            Session = tester.Session,
            ChatId = id.ChatId,
            LocalId = id.LocalId,
            Text = text,
        };
        return tester.Commander.Call(cmd);
    }

    public static Task RemoveTextEntry(this IWebTester tester, ChatEntryId id)
        => tester.Commander.Call(new Chats_RemoveEntry {
            Session = tester.Session,
            ChatId = id.ChatId,
            LocalId = id.LocalId,
        });

    public static async Task<ChatEntry[]> CreateTextEntries(this IWebTester tester, ChatId chatId, string textPrefix, int entryCount)
    {
        // Posts strictly one-by-one: Collect(1) keeps 2 tasks in flight, so entry LIDs
        // could come out non-monotonic vs the array order, which tests rely on.
        var entries = new ChatEntry[entryCount];
        for (var i = 0; i < entryCount; i++)
            entries[i] = await tester.CreateTextEntry(chatId, $"{textPrefix} {i + 1}").ConfigureAwait(false);
        return entries;
    }

    public static async Task<StreamingEntry> CreateStreamingEntry(
        this IWebTester tester,
        ChatId chatId,
        Language language,
        Moment? beginsAt = null,
        string content = "",
        CancellationToken cancellationToken = default)
    {
        var clocks = tester.AppServices.Clocks();
        var effectiveBeginsAt = beginsAt ?? clocks.SystemClock.Now;
        var author = await tester.GetOwnAuthor(chatId, cancellationToken).Require();
        var streamId = StreamId.New(NodeRef.ThisNodeAlias).Value;
        var textEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(ChatEntryId.New(chatId, 0),
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    ContentStreamId = streamId,
                    Audio = new ChatEntryAudio { StreamId = streamId },
                    Content = content,
                    BeginsAt = effectiveBeginsAt,
                })),
            cancellationToken: cancellationToken);
        var entryLanguage = await tester
            .CreateEntryLanguage(textEntry.Id, language, textEntry.ContentHash, cancellationToken)
            .ConfigureAwait(false);
        return new (textEntry, entryLanguage);
    }

    public static async Task<StreamingEntry> FinalizeStreamingEntry(
        this IWebTester tester,
        StreamingEntry streamingEntry,
        string text,
        CancellationToken cancellationToken = default)
    {
        var clocks = tester.AppServices.Clocks();
        var now = clocks.SystemClock.Now;
        var textEntry = streamingEntry.ChatEntrySlim;

        textEntry = await tester.Commander.Call(new ChatsBackend_ChangeEntry(textEntry.Id, textEntry.Version, Change.Update(new ChatEntryDiff {
            Content = text,
            ContentStreamId = "",
            Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:mediaid") },
            EndsAt = now,
        })), cancellationToken);

        var entryLanguage = await tester.UpdateEntryLanguage(
            streamingEntry.EntryLanguage with { EntryContentHash = textEntry.ContentHash },
            cancellationToken);
        return new (textEntry, entryLanguage);
    }
}

public sealed record StreamingEntry(ChatEntry ChatEntrySlim, ChatEntryLanguage EntryLanguage);
