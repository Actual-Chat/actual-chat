using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests;

public sealed class ConversationSummarizerLanguageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId ChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId AuthorId = AuthorId.New(ChatId, 1);

    [Fact]
    public async Task ShouldDetectLanguageOfEntriesWithoutStoredOne()
    {
        // arrange
        var (summarizer, detector) = CreateSummarizer();
        var entries = new[] { Entry(1, "Привет"), Entry(2, "Как дела"), Entry(3, "Всё хорошо") };

        // act
        var language = await summarizer.GetMostCommonLanguage(ChatId, entries, CancellationToken.None);

        // assert
        language.Should().Be(Language.Parse("ru"));
        detector.DetectedLids.Should().BeEquivalentTo([1L, 2L, 3L]);
    }

    [Fact]
    public async Task ShouldSkipDetectionForEntriesWithoutLetters()
    {
        // arrange
        var (summarizer, detector) = CreateSummarizer();
        var entries = new[] { Entry(1, "Привет"), Entry(2, "...") };

        // act
        await summarizer.GetMostCommonLanguage(ChatId, entries, CancellationToken.None);

        // assert
        detector.DetectedLids.Should().BeEquivalentTo([1L]);
    }

    [Fact]
    public async Task ShouldLimitDetectionToLatestEntries()
    {
        // arrange
        var (summarizer, detector) = CreateSummarizer();
        var entries = Enumerable.Range(1, ConversationSummarizer.MaxDetectedEntryCount + 3)
            .Select(i => Entry(i, "Привет"))
            .ToList();

        // act
        await summarizer.GetMostCommonLanguage(ChatId, entries, CancellationToken.None);

        // assert
        detector.DetectedLids.Should().HaveCount(ConversationSummarizer.MaxDetectedEntryCount);
        detector.DetectedLids.Should().Contain(entries[^1].LocalId);
    }

    // Private methods

    private static (ConversationSummarizer Summarizer, RussianDetector Detector) CreateSummarizer()
    {
        var detector = new RussianDetector();
        var services = new ServiceCollection();
        var commander = services.AddCommander();
        services.AddFusion();
        services.AddSingleton<IChatEntryLanguagesBackend, EmptyTileLanguagesBackend>();
        services.AddSingleton(detector);
        commander.AddHandlers<RussianDetector>();
        var summarizer = new ConversationSummarizer(
            new ConversationSummarizer.Options(),
            services.BuildServiceProvider());
        return (summarizer, detector);
    }

    private static ChatEntrySlim Entry(long lid, string content)
        => new (lid, content, AuthorId, Moment.EpochStart, null, false, null, false);

    // Nested types

    private sealed class RussianDetector : ICommandHandler<ChatEntryLanguagesBackend_Detect>
    {
        public List<long> DetectedLids { get; } = [];

        public Task OnCommand(
            ChatEntryLanguagesBackend_Detect command,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            lock (DetectedLids)
                DetectedLids.Add(command.Id.LocalId);
            context.SetResult(new ChatEntryLanguage(command.Id) { Languages = [Language.Parse("ru")] });
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyTileLanguagesBackend : IChatEntryLanguagesBackend
    {
        public Task<ChatLanguageTile> GetTile(
            ChatId chatId,
            Range<long> lidTileRange,
            CancellationToken cancellationToken)
            => Task.FromResult(new ChatLanguageTile(lidTileRange, []));

        public Task<ChatEntryLanguage?> OnDetect(
            ChatEntryLanguagesBackend_Detect command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ChatEntryLanguage?> OnChange(
            ChatEntryLanguagesBackend_Change command,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
