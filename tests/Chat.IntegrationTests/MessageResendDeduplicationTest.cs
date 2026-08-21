using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class MessageResendDeduplicationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IChats Chats => Tester.AppServices.GetRequiredService<IChats>();

    [Fact]
    public async Task ResendWithSameUuidDoesNotPostSecondMessage()
    {
        // arrange — SendingMessages resends the request's Uuid after its 15s timeout,
        // so the same command reaching the server twice must post one message
        await Tester.SignInAsUniqueAlice();
        var (chat, _) = await Tester.CreateAndGetChat(isPublicChat: true);
        var session = Tester.Session;
        var commander = Tester.Commander;
        var command = new Chats_UpsertEntry {
            Uuid = ApiCommand.NewUuid(),
            Session = session,
            ChatId = chat.Id,
            LocalId = null,
            Text = "Sent once",
        };

        // act
        var first = await commander.Call(command);
        var second = await commander.Call(command);

        // assert — the resend replayed the stored result
        second.Id.Should().Be(first.Id);
        second.LocalId.Should().Be(first.LocalId);

        // control — a fresh Uuid is a new intent and does post a second message
        var third = await commander.Call(command with { Uuid = ApiCommand.NewUuid(), Text = "Sent twice" });
        third.LocalId.Should().BeGreaterThan(first.LocalId);

        await ComputedTest.When(async ct => {
            var idRange = await Chats.GetIdRange(session, chat.Id, ct);
            var entries = new List<ChatEntry>();
            await foreach (var entry in Chats.NewEntryReader(session, chat.Id).Read(idRange, ct).ConfigureAwait(false))
                entries.Add(entry);
            entries.Count(x => x.Content == "Sent once").Should().Be(1);
            entries.Count(x => x.Content == "Sent twice").Should().Be(1);
        });
    }
}
