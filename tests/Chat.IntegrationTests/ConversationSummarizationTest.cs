using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ConversationSummarizationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void ShouldLimitMentionsToKnownAuthors()
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
    public void ShouldCapConversationSummaryOutputLength()
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
        // act
        var formatter = AppHost.Services.GetRequiredService<IChatDialogFormatter>();

        // assert
        formatter.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldFormatAndSummarizeEntries()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);
        var messages = new[] { "Hello everyone!", "How is the project going?", "Let's discuss the roadmap." };
        var entries = new List<ChatEntry>();
        foreach (var message in messages) {
            var cmd = new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = message };
            var entry = await commander.Call(cmd);
            entries.Add(entry);
        }
        var textEntries = entries.Select(e => new ChatEntrySlim(e)).ToList();

        // act
        var formatter = tester.AppServices.GetRequiredService<IChatDialogFormatter>();
        var formatted = await formatter.EntriesToText(textEntries);
        var summarizer = tester.AppServices.GetRequiredService<IConversationSummarizer>();
        var result = await summarizer.Summarize(textEntries, default);

        // assert
        foreach (var msg in messages)
            formatted.Should().Contain(msg);
        result.HasResult.Should().BeTrue();
        result.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldExcludeRemovedEntriesFromListNewEntries()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var entries = await tester.CreateTextEntries(chatId, "message", 3);
        var removedEntry = entries[1];
        await tester.RemoveTextEntry(removedEntry.Id);

        // act
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();
        var listed = await chatsBackend.ListNewEntries(chatId, 0, 100, default);

        // assert
        var listedLids = listed.Select(e => e.LocalId).ToArray();
        listedLids.Should().NotContain(removedEntry.LocalId);
        listedLids.Should().Contain(entries[0].LocalId);
        listedLids.Should().Contain(entries[2].LocalId);
    }

    [Fact]
    public async Task ShouldExcludeRemovedEntriesFromSummary()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var entries = await tester.CreateTextEntries(chatId, "message", 3);
        // One-per-entry lid ranges keep async system entries (e.g. "joined") out of the count
        var lidRanges = entries
            .Select(e => new Range<long>(e.LocalId, e.LocalId + 1))
            .ToArray();
        await tester.RemoveTextEntry(entries[1].Id);

        // act
        var commander = tester.AppServices.Commander();
        var conversation = await commander.Call(new ConversationBackend_Summarize(chatId, lidRanges));

        // assert
        conversation.MessageCount.Should().Be(2);
        conversation.AuthorIds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldRemoveConversationWhenAllItsEntriesAreRemoved()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var entries = await tester.CreateTextEntries(chatId, "message", 2);
        var lidRanges = entries
            .Select(e => new Range<long>(e.LocalId, e.LocalId + 1))
            .ToArray();
        var commander = tester.AppServices.Commander();
        var conversation = await commander.Call(new ConversationBackend_Summarize(chatId, lidRanges));
        conversation.Should().NotBeNull();
        var conversationsBackend = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        foreach (var entry in entries)
            await tester.RemoveTextEntry(entry.Id);
        await commander.Call(new ConversationBackend_Summarize(chatId, lidRanges));

        // assert
        var removed = await conversationsBackend.Get(conversation.Id, default);
        removed.Should().BeNull();
    }

    [Fact]
    public async Task ShouldResummarizeConversationOnEntryRemoval()
    {
        // arrange
        await using var appHost = await NewAppHost("resummarize-on-delete", options => options with {
            UseNatsQueues = false,
            ConfigureHost = (_, cfg) => {
                cfg.AddInMemory<ChatSettings>((x => x.IsSummarizationEnabled, "true"));
                cfg.AddInMemory<CoreServerSettings>((x => x.OpenAIKey, "test-key"));
                var delayKey = $"{nameof(ChatSettings)}:{nameof(ChatSettings.Summarization)}"
                    + $":{nameof(SummarizationSettings.ResummarizationDelay)}";
                cfg.AddInMemoryCollection((delayKey, "00:00:01"));
            },
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<IConversationSummarizer, ConversationSummarizerStub>());
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var entries = await tester.CreateTextEntries(chatId, "message", 3);
        // Let async follow-up commands (e.g. the system "joined" entry) settle: the removal-triggered
        // resummary walks the conversation's contiguous lid range, so a late entry would shift counts
        await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(1), default);
        var lidRange = new Range<long>(entries[0].LocalId, entries[2].LocalId + 1);
        var commander = tester.AppServices.Commander();
        var conversation = await commander.Call(new ConversationBackend_Summarize(chatId, [lidRange]));
        var baselineCount = conversation.MessageCount;
        baselineCount.Should().BeGreaterThanOrEqualTo(3);
        var conversationsBackend = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await tester.RemoveTextEntry(entries[1].Id);

        // assert
        await ComputedTest.When(async ct => {
            var updated = await conversationsBackend.Get(conversation.Id, ct);
            updated.Should().NotBeNull();
            updated!.MessageCount.Should().Be(baselineCount - 1);
        }, TimeSpan.FromSeconds(30));
    }
}
