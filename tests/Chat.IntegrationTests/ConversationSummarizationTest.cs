using ActualChat.Chat.ML;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ConversationSummarizationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void LimitsMentionsToKnownAuthors()
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var authorIds = Enumerable.Range(1, ConversationSummarizer.MaxMentionCount + 5)
            .Select(i => AuthorId.New(chatId, i))
            .ToArray();
        var unknownAuthorId = AuthorId.New(chatId, 100);
        var unknownMention = "@" + MentionRef.NewAuthor(unknownAuthorId).Value;
        var userMention = "@" + MentionRef.NewUser(UserId.New()).Value;
        var knownMentions = authorIds.Select(id => "@" + MentionRef.NewAuthor(id).Value);
        var summary = string.Join(" ", knownMentions.Prepend(userMention).Prepend(unknownMention));
        var input = new ConversationSummary("", "", summary);

        // act
        var result = ConversationSummarizer.SanitizeSummary(input, authorIds);

        // assert
        var mentionIds = MentionExtractor.Instance.GetMentionIds(new MarkupParser().Parse(result.Summary));
        mentionIds.Should().HaveCount(ConversationSummarizer.MaxMentionCount);
        mentionIds.Should().OnlyContain(id => authorIds.Select(MentionRef.NewAuthor).Contains(id));
    }

    [Fact]
    public void CapsConversationSummaryOutputLength()
    {
        // arrange
        var input = new ConversationSummary(
            new string('t', ConversationSummarizer.MaxOutputLength),
            new string('d', ConversationSummarizer.MaxOutputLength),
            new string('s', ConversationSummarizer.MaxOutputLength));

        // act
        var result = ConversationSummarizer.SanitizeSummary(input, []);

        // assert
        (result.Title.Length + result.Description.Length + result.Summary.Length)
            .Should().BeLessThanOrEqualTo(ConversationSummarizer.MaxOutputLength);
    }

    [Fact]
    public async Task ShouldResolveChatDialogFormatter()
    {
        // Resolve IChatDialogFormatter — fails if registration is missing
        var formatter = AppHost.Services.GetRequiredService<IChatDialogFormatter>();
        formatter.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldFormatAndSummarizeEntries()
    {
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;

        var (chatId, _) = await tester.CreateChat(true);

        // Post messages
        var messages = new[] { "Hello everyone!", "How is the project going?", "Let's discuss the roadmap." };
        var entries = new List<ChatEntry>();
        foreach (var message in messages) {
            var cmd = new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = message };
            var entry = await commander.Call(cmd);
            entries.Add(entry);
        }

        var textEntries = entries.Select(e => new ChatEntrySlim(e)).ToList();

        // Test ChatDialogFormatter
        var formatter = tester.AppServices.GetRequiredService<IChatDialogFormatter>();
        var formatted = await formatter.EntriesToText(textEntries);
        foreach (var msg in messages)
            formatted.Should().Contain(msg);

        // Test ConversationSummarizer (uses stub in test env)
        var summarizer = tester.AppServices.GetRequiredService<IConversationSummarizer>();
        var result = await summarizer.Summarize(textEntries, default);
        result.HasResult.Should().BeTrue();
        result.Summary.Should().NotBeNull();
    }
}
