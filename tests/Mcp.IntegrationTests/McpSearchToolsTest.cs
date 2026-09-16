using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpSearchCollection))]
public class McpSearchToolsTest(McpSearchCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpSearchCollection.AppHostFixture>(fixture, @out)
{
    private const int IndexingAttempts = 300;

    [LocalFact("needs OpenSearch")]
    public async Task SearchMessagesShouldFindPostedTextInChat()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var (otherChatId, _) = await Tester.CreateChat(isPublicChat: true);
        var marker = $"zebrafish{RandomStringGenerator.Default.Next(6)}";
        var entry = await Tester.CreateTextEntry(chatId, $"the {marker} swims");
        await Tester.CreateTextEntry(otherChatId, $"another {marker} here");
        var client = await CreateClient();

        // act
        var found = await WaitFor(
            () => CallTool<McpFoundMessage[]>(client, "search_messages", new { query = marker, chatId = chatId.Value }),
            r => r.Length > 0, IndexingAttempts);
        var foundEverywhere = await WaitFor(
            () => CallTool<McpFoundMessage[]>(client, "search_messages", new { query = marker }),
            r => r.Length == 2, IndexingAttempts);

        // assert
        found.Should().ContainSingle().Which.EntryId.Should().Be(entry.LocalId);
        found.Single().ChatId.Should().Be(chatId.Value);
        foundEverywhere.Should().HaveCount(2);
    }

    [LocalFact("needs OpenSearch")]
    public async Task SearchContactsShouldFindChatByTitle()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var title = $"Quokka {RandomStringGenerator.Default.Next(6)}";
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: title);
        var client = await CreateClient();

        // act
        var found = await WaitFor(
            () => CallTool<McpFoundContact[]>(client, "search_contacts", new { query = title, scope = "groups" }),
            r => r.Any(c => c.Id == chatId.Value), IndexingAttempts);

        // assert
        found.Should().Contain(c => c.Id == chatId.Value && c.Kind == "chat" && c.Title == title);
    }
}
