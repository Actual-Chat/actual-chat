using ActualChat.Chat.ML;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ConversationSummarizationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
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
            var cmd = new Chats_UpsertTextEntry(session, chatId, null) { Text = message };
            var entry = await commander.Call(cmd);
            entries.Add(entry);
        }

        var textEntries = entries.Select(e => new TextEntry(e)).ToList();

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
