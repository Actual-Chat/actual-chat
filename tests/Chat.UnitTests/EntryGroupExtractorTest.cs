using System.IO.Compression;
using ActualChat.Chat.ML;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ActualChat.Chat.UnitTests;

public class EntryGroupExtractorTest(ITestOutputHelper @out, ILogger<EntryGroupExtractor> log) : TestBase(@out, log)
{
    private readonly AuthorId _authorId = AuthorId.New(GroupChatId.New(), 1);

    [Fact]
    public void ExtractGroups_EmptyEntries_ReturnsEmpty()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = new ExtractorState(null, null);

        // Act
        var result = extractor.ExtractGroups(initialState, []);

        // Assert
        result.Groups.Should().BeEmpty();
        result.ReplySequences.Should().BeEmpty();
    }

    [Fact]
    public void ExtractGroups_SystemEntry_IsIgnored()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = new ExtractorState(null, null);
        var entries = new List<ChatEntrySlim> {
            new (0, "System message", _authorId, DateTime.Now, null, false, null, false)
        };

        // Act
        var result = extractor.ExtractGroups(initialState, entries);

        // Assert
        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public void ExtractGroups_PauseExceedsMax_CompletesGroup()
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
        var entries = new List<ChatEntrySlim>
        {
            new (1, "Entry 1", _authorId, baseTime, null, false, null, false),
            new (2, "Entry 2", _authorId, baseTime.AddMinutes(1), null, false, null, false),
            new (3, "Entry 3", _authorId, baseTime.AddHours(13 + 1), null, false, null, false), // 13 hours after entry2
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.Groups.Should().ContainSingle();
        var group = result.Groups[0];
        group.Entries.Should().HaveCount(2);
        group.Entries[0].Content.Should().Be(entries[0].Content);
        group.Entries[1].Content.Should().Be(entries[1].Content);

        // Check state: currentChunk contains the third entry
        result.State.CurrentChunk.Should().NotBeNull();
        result.State.CurrentChunk!.Entries.Should().ContainSingle();
        result.State.CurrentChunk.Entries[0].Content.Should().Be(entries[2].Content);
    }

    [Fact]
    public void ExtractGroups_ChunkFillsAndMerges_CompletesGroupWhenWordCountMet()
    {
        // Arrange
        var groupWordCount = 100;
        var entries = Enumerable.Range(0, 10)
            .Select(i => new ChatEntrySlim(
                i + 1,
                string.Join(" ", Enumerable.Repeat("word", 10)),
                _authorId,
                DateTime.Now.AddMinutes(i),
                null,
                false,
                null,
                false))
            .ToList();

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log, groupWordCount);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.Groups.Should().ContainSingle();
        result.Groups[0].Entries.Should().HaveCount(10);
    }

    [Fact]
    public void ExtractGroups_ChunkNotMergedDueToLowSimilarity_CompletesGroup()
    {
        // Arrange
        var groupWordCount = 100;
        var entries = Enumerable.Range(0, 10)
            .Select(i => new ChatEntrySlim(i + 1, string.Join(" ", Enumerable.Repeat("word", 10)), _authorId, DateTime.Now.AddMinutes(i), null, false, null, false))
            .ToList();

        // Use a custom embeddings calculator with low similarity
        var lowSimilarityCalculator = new LowSimilarityEmbeddingsCalculator();
        var extractor = new EntryGroupExtractor(lowSimilarityCalculator, log, groupWordCount);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.Groups.Should().ContainSingle();
        result.Groups[0].Entries.Should().HaveCount(10);
    }

    [Fact]
    public void ExtractGroups_ReplySequenceWithinGroup_IsNotCaptured()
    {
        // Arrange
        var entries = new List<ChatEntrySlim>
        {
            new(1, "Entry 1", _authorId, DateTime.Now, null, false, null, false),
            new(2, "Reply to Entry 1", _authorId, DateTime.Now.AddSeconds(10), null, false, 1, false),
            new(3, "Entry 2", _authorId, DateTime.Now.AddMinutes(1), null, false, null, false)
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.ReplySequences.Should().BeEmpty();
        result.Groups.Should().BeEmpty();
        result.State.CurrentGroup.Should().NotBeNull();
        result.State.CurrentChunk.Should().NotBeNull();
        result.State.CurrentChunk!.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void ExtractGroups_ReplySequenceExceedsLimit_CompletesSequence()
    {
        // Arrange
        var entries = new List<ChatEntrySlim>
        {
            new(2, "Entry 1", _authorId, DateTime.Now, null, false, null, false),
            new(3, "Reply to Entry 1", _authorId, DateTime.Now.AddSeconds(10), null, false, 1, false),
            new(4, "Comment to reply", _authorId, DateTime.Now.AddSeconds(20), null, false, null, false),
            new(5, "Another comment", _authorId, DateTime.Now.AddSeconds(30), null, false, null, false),
            new(6, "Another comment 2", _authorId, DateTime.Now.AddSeconds(30), null, false, null, false),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.ReplySequences.Should().ContainSingle();
        var replySequence = result.ReplySequences[0];
        replySequence.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void ExtractGroups_ReplySequenceWithLongPause_CompletesSequence()
    {
        // Arrange
        var entries = new List<ChatEntrySlim>
        {
            new(2, "Entry 1", _authorId, DateTime.Now, null, false, null, false),
            new(3, "Reply to Entry 1", _authorId, DateTime.Now.AddSeconds(10), null, false, 1, false),
            new(4, "Late Reply", _authorId, DateTime.Now.AddMinutes(10), null, false, null, false),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = extractor.ExtractGroups(new ExtractorState(null, null), entries);

        // Assert
        result.ReplySequences.Should().ContainSingle();
        var replySequence = result.ReplySequences[0];
        replySequence.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExtractGroups_ExtractGroupsFromRealData_ShouldProvideConversations()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = new ExtractorState(null, null);
        var entries = await ReadTestEntries("./data/chat_entries.zip");

        // Act
        var result = extractor.ExtractGroups(initialState, entries);

        // Assert
        result.Groups.Should().NotBeEmpty();
        result.Groups.Count.Should().BeLessThan(40);
        result.Groups.Should().AllSatisfy(group => group.Entries.Should().NotBeEmpty());
    }

    [Fact]
    public void ExtractGroups_ExtractGroupsWithBrokenState_ShouldProvideConversations()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = JsonSerializer.Deserialize<ExtractorState>(
            """{"CurrentGroup":{"Entries":[],"AuthorActivity":{},"WordCount":0,"Embeddings":[],"AveragePauseBetweenEntries":0,"MinLid":9223372036854775807,"MaxLid":0,"Text":""},"CurrentChunk":{"Entries":[{"LocalId":128581,"Content":"у, как его? Это слушай Андрюха Давайте не будем повторять то, что мы уже разбирали анонс. Ну что, либо исчезает, да, либо как бы, Когда двое пишут там знаешь, э, ну, оно дёргается. Просто немножко пристроено. Ну, то есть, потому что два сообщения растут и Ну короче, мы это неправильно вот на Айфоне бак намного серьёзнее.","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:39:00.7622090Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null}],"AuthorActivity":{"052w3sgrad:40":56},"WordCount":56,"Embeddings":[],"AveragePauseBetweenEntries":0,"MinLid":128581,"MaxLid":128581,"Text":"у, как его? Это слушай Андрюха Давайте не будем повторять то, что мы уже разбирали анонс. Ну что, либо исчезает, да, либо как бы, Когда двое пишут там знаешь, э, ну, оно дёргается. Просто немножко пристроено. Ну, то есть, потому что два сообщения растут и Ну короче, мы это неправильно вот на Айфоне бак намного серьёзнее.\n"},"MaxLid":128581}""",
            JsonSerializerOptions.Default);
        var entries = JsonSerializer.Deserialize<List<ChatEntrySlim>>(
            """[{"LocalId":128582,"Content":"Ну вот, наверное, Может им еще один бак Report сделать тогда на эту тему. Я не знаю, потому что они Может я просто почитал тот про который писали он вообще специфичный сильно. То есть у нас-то, оно же без аут и так далее. То есть надо писать, что на самом деле, а проявляется и без","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:39:18.9511930Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null},{"LocalId":128583,"Content":"По поводу компоненты, которые в этот выдаёт.\r\nРезультаты поиска\r\nВот этого достаточно, чтобы\r\nНу то, что там сейчас передаётся.\r\nДля разделения на Плейс и чаты. Ну то есть, когда здесь message, он Search матч\r\nЕсть я понимаю, что из\r\nИз этого мессендже я могу вытащить относится он к чату и к Plays и так далее. А когда title Search\r\nКороче надо ли здесь ещё Play ID Или этого всего достаточно, чтобы понять.\r\nКто нашлось название Place и название чата?\r\nНу у нас же здесь передается чат ID","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:39:35.6673230Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null},{"LocalId":128584,"Content":"А вот, что я хотел спросить по поводу юаня. Я как бы это, наверное, не очень важно, но подбешивает, когда короче, как имиджи отачи подгружаешь на iPhone на этом на Андроиде неважно на Айфоне э, и клавиатура открывается и у тебя, короче, у нас скрывается этот индикатор загрузки для маленьких вариантов картины и типа, как бы тебе надо обратно сложить клавиатуру, чтобы посмотреть загрузилось она или нет, когда-то картинки что-нибудь подписываешь ещё комментарии, и у нас вот в маленьком варианте нет этого индикатора. Он там скрыт и я, ну, видимо, для экономии места. Это было сделано. Не знаю. Вот","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:39:49.9768670Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null},{"LocalId":128585,"Content":"Привет я пока тут.\r\nМы вообще договаривались в понедельник сделать. Вечером нашим звонок. Да чтобы Саша приходил. Вот все остальные звонки перенести на час раньше. То есть, чтоб больше вероятность было попадать, так как по московскому времени только я сейчас вроде 8 утра мне не проблема присоединиться. Я не знаю, как тебе удобно сейчас. Сейчас сколько время?","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:40:08.9501850Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null},{"LocalId":128586,"Content":"Мы вообще договаривались в понедельник сделать. Вечером нашим звонок. Да чтобы Саша приходил. Вот все остальные звонки перенести на час раньше. То есть, чтоб больше вероятность было попадать, так как по московскому времени только я сейчас вроде 8 утра мне не проблема присоединиться. Я не знаю, как тебе удобно сейчас. Сейчас сколько время?","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:40:34.2247310Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null},{"LocalId":128587,"Content":"Что, давайте я начну начну, потому что у меня нихуя никаких новостей за сегодня нет. Что-то мы отходили От вчерашнего, а потом нас соседи в гости нас созвали. И вот я только что вернулся. Вот, так что извиняюсь. Я думаю, наверное, надо считать, что я пока не пришёл отпуске. Э-э. Вот э-э. Ну, ну, я думаю, завтра что-нибудь уже сделаю. Я просто короче Как-то получилось что куча. Всего подряд и Ну вот","AuthorId":"052w3sgrad:40","BeginsAt":"2025-06-19T07:47:47.4541440Z","EndsAt":null,"IsTranscript":false,"RepliedEntryLid":null}]""",
            JsonSerializerOptions.Default);

        // Act
        var result = extractor.ExtractGroups(initialState, entries);

        // Assert
        result.Groups.Should().NotBeEmpty();
    }

    [Fact]
    public void ExtractGroups_TwoSubsequentCallsWithMultipleEntriesWithoutPause_ProducesSingleGroup()
    {
        // Arrange

        var now = DateTimeOffset.UtcNow;
        var messageCounter = 1;

        var firstBatch = Enumerable.Range(1, 10)
            .Select(i => new ChatEntrySlim(i, $"Message {messageCounter++} " + string.Join(" ", Enumerable.Repeat("word", 10)), _authorId, now.AddSeconds(i).UtcDateTime, null, true, null, false))
            .ToList();

        var secondBatch = Enumerable.Range(1, 10)
            .Select(i => new ChatEntrySlim(i + firstBatch.Count, $"Message {messageCounter++} " + string.Join(" ", Enumerable.Repeat("word", 10)), _authorId, now.AddSeconds(i).UtcDateTime, null, true, null, false))
            .ToList();

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act

        var (state1, groups1, _) = extractor.ExtractGroups(null, firstBatch);
        var (state2, groups2, _)  = extractor.ExtractGroups(state1, secondBatch);

        // Assert

        groups1.Should().BeEmpty();
        groups2.Should().BeEmpty();

        var allEntries = firstBatch.Concat(secondBatch);

        var currentGroup = state2.CurrentGroup!.AddRange(state2.CurrentChunk!.Entries).Build();
        currentGroup.Entries.Should().HaveCount(20, "group should contain all entries from both batches");
        currentGroup.Entries.Should().BeEquivalentTo(allEntries, options => options.WithStrictOrdering());
    }



    [Fact(Skip = "For exploratory purposes only")]
    public async Task ExtractGroups_ExtractGroupsFromRealDataWithEmbeddings_ShouldProvideConversations()
    {
        // Arrange
        var settings = new EmbeddingSettings {
            // PredictionsUri = "http://localhost:28080/predictions/Alibaba-NLP_gte-multilingual-base",
            PredictionsUri = "http://localhost:8000/v1/embeddings", // Experimenting with another (much faster!) model served with vllm
        };
        var httpClientFactory = new ServiceCollection().AddHttpClient()
            .BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var extractor = new EntryGroupExtractor(new EmbeddingsCalculator(settings, httpClientFactory), log);
        var initialState = new ExtractorState(null, null);
        var entries = await ReadTestEntries("./data/chat_entries.zip");

        // Act
        var result = extractor.ExtractGroups(initialState, entries);

        // Assert
        result.Groups.Should().NotBeEmpty();
        result.Groups.Count.Should().BeLessThan(50);
        result.Groups.Should().AllSatisfy(group => group.Entries.Should().NotBeEmpty());
    }


    private async Task<IReadOnlyCollection<ChatEntrySlim>> ReadTestEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.First();
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<List<JsonTextEntry>>(json)!
            .Select(jsonEntry => new ChatEntrySlim(
                jsonEntry.LocalId,
                jsonEntry.Content,
                AuthorId.Parse(jsonEntry.AuthorId),
                new Moment(jsonEntry.BeginsAt),
                jsonEntry.EndsAt != null ? new Moment(jsonEntry.EndsAt.Value) : null,
                jsonEntry.AudioEntryId != null,
                jsonEntry.RepliedChatEntryId,
                false))
            .ToList();
    }

    private sealed class HighSimilarityEmbeddingsCalculator : IEmbeddingsCalculator
    {
        public Task<double[]> CalculateVector(string text, CancellationToken cancellationToken)
            => Task.FromResult(new [] { 0.1, 0.2 });

        public double CosineSimilarity(double[] a, double[] b)
            => 0.95;

        public double[] Normalize(double[] vector)
            => vector;
    }

    private sealed class LowSimilarityEmbeddingsCalculator : IEmbeddingsCalculator
    {
        public Task<double[]> CalculateVector(string text, CancellationToken cancellationToken)
            => Task.FromResult(new[] { 0.3, 0.4 }); // Different from HighSimilarityEmbeddingsCalculator

        public double CosineSimilarity(double[] a, double[] b)
            => 0.85; // Below the 0.9 threshold

        public double[] Normalize(double[] vector)
            => vector;
    }

    public record JsonTextEntry(
        [property: JsonProperty("local_id")] long LocalId,
        [property: JsonProperty("author_id")] string AuthorId,
        [property: JsonProperty("begins_at")] DateTime BeginsAt,
        [property: JsonProperty("ends_at")] DateTime? EndsAt,
        [property: JsonProperty("duration")] double Duration,
        [property: JsonProperty("content")] string Content,
        [property: JsonProperty("audio_entry_id")] long? AudioEntryId,
        [property: JsonProperty("replied_chat_entry_id")] long? RepliedChatEntryId,
        [property: JsonProperty("is_system_entry")] bool IsSystemEntry
    );
}
