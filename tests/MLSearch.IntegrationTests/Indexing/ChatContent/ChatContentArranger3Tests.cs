using ActualChat.Chat;
using ActualChat.Chat.ML;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.IntegrationTests.Indexing.ChatContent;

public class ChatContentArranger3Tests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Run explicitly")]
    public async Task IndexChat()
    {
        var authorRetriever = new DelegateAuthorNameRetriever(c => {
            string authorName = c.LocalId switch {
                28 or 30 => "AY",
                1 => "DF",
                3 or 29 => "AK",
                4 => "FR",
                _ => "author-" + c.LocalId
            };
            return Task.FromResult(authorName);
        });
        var chatDialogFormatter = new ChatDialogFormatter(authorRetriever);
        var tailDocuments = new Dictionary<SourceEntries, ChatSlice>();
        var documentLoader = new DocumentLoader(tailDocuments);
        var embeddingsCalculator = new EmbeddingsCalculator(new EmbeddingSettings());
        var contentArranger = new ChatContentArranger3(documentLoader, embeddingsCalculator, chatDialogFormatter);
        //contentArranger.DebugLog = c => WriteLine(c);
        var fragmentId = 0;

        var enumerator = GetEntries().GetEnumerator();
        using var enumerator1 = enumerator as IDisposable;
        var i = 0;
        while (i < 10) {
            var batchSize = 60;
            var batch = new List<ChatEntry>();
            while (enumerator.MoveNext()) {
                batch.Add(enumerator.Current);
                if (batch.Count >= batchSize)
                    break;
            }

            WriteLine("-- Starting new arrange iteration");
            var result = contentArranger.Arrange(batch, tailDocuments.Values.ToImmutableArray(), default);

            var outDocuments = new Dictionary<SourceEntries, ChatSlice>();
            await foreach (var entrySet in result) {
                var chatSliceMetadata = new ChatSliceMetadata {
                    ChatEntries = [..entrySet.Entries.Select(c => new ChatSliceEntry(c.Id.ToTextEntryId(), c.LocalId, c.Version))]
                };
                var text = await chatDialogFormatter.EntriesToText(entrySet.Entries);
                outDocuments.Add(entrySet, new ChatSlice(chatSliceMetadata, text));
                var docId = entrySet.Entries.Min(c => c.LocalId);
                WriteLine("----------------------------");
                WriteLine($"Fragment {fragmentId}. DocId: {docId}, Entry count: {entrySet.Entries.Count}, Word count: {ChatContentArranger3.CountWords(text)}");
                WriteLine(text);
                WriteLine("----------------------------");
                fragmentId++;
            }
            i++;
            tailDocuments.Clear();
            tailDocuments.AddRange(outDocuments);
        }
    }

    private IEnumerable<ChatEntry> GetEntries()
    {
        var path = System.Environment.GetEnvironmentVariable("ActualChat_TestData");
        path ??= @"C:\\TestData\";
        path = Path.Combine(path, "dev_chat_entries.csv");
        using var csvParser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path);
        csvParser.CommentTokens = [ "#" ];
        csvParser.SetDelimiters(",");
        csvParser.HasFieldsEnclosedInQuotes = true;
        // Skip the row with the column names
        csvParser.ReadLine();

        while (!csvParser.EndOfData) {
            // Read current line fields, pointer moves to the next line.
            var fields = csvParser.ReadFields()!;
            var id = fields[0];
            var version = fields[3];
            var authorId = fields[5];
            var beginsAt = fields[6];
            var content = fields[12];
            var isSystemEntry = fields[20];
            if (bool.Parse(isSystemEntry))
                continue;

            var chatEntry = new ChatEntry(ChatEntryId.Parse(id), long.Parse(version)) {
                AuthorId = AuthorId.Parse(authorId),
                BeginsAt = Moment.Parse(beginsAt),
                Content = content,
            };
            yield return chatEntry;
        }
    }

    private class DocumentLoader(Dictionary<SourceEntries, ChatSlice> tailDocuments) : IDocumentEntriesLoader
    {
        public ValueTask<IReadOnlyList<ChatEntry>> LoadTailEntries(ChatSlice tailDocument, CancellationToken cancellationToken)
        {
            var chatSlice = tailDocuments
                .Where(c => c.Value == tailDocument)
                .Select(c => c.Key).FirstOrDefault();
            var result = chatSlice?.Entries ?? [];
            return ValueTask.FromResult(result);
        }
    }
}
