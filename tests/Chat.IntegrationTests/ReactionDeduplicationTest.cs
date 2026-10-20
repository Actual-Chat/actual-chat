using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ReactionDeduplicationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IReactions Reactions => Tester.AppServices.GetRequiredService<IReactions>();

    [Fact]
    public async Task DuplicateReactionCommandIsDeduplicated()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chat, _) = await Tester.CreateAndGetChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chat.Id, "Hello");
        var entryId = entry.Id;
        var session = Tester.Session;
        var commander = Tester.Commander;
        var uuid = ApiCommand.NewUuid();

        // act — react, then send the SAME Uuid with a different emoji (must be deduped)
        var lolCmd = new Reactions_React {
            Uuid = uuid, Session = session, Reaction = NewReaction(entryId, Emojis.Lol),
        };
        var coolCmd = new Reactions_React {
            Uuid = uuid, Session = session, Reaction = NewReaction(entryId, Emojis.Cool),
        };
        await commander.Call(lolCmd);
        await commander.Call(coolCmd);

        // assert — the duplicate never ran; only Lol is there (Cool was not applied)
        await TestWait.When(async ct => {
            var reactions = await Reactions.ListOwn(session, entryId, ct);
            reactions.Select(x => x.Emoji).Should().Equal([Emojis.Lol]);
        });

        // control — a fresh (auto-generated) Uuid does add Cool next to Lol
        await commander.Call(new Reactions_React { Session = session, Reaction = NewReaction(entryId, Emojis.Cool) });
        await TestWait.When(async ct => {
            var reactions = await Reactions.ListOwn(session, entryId, ct);
            reactions.Select(x => x.Emoji).Should().BeEquivalentTo([Emojis.Lol, Emojis.Cool]);
        });
    }

    private static Reaction NewReaction(ChatEntryId entryId, Emoji emoji)
        => new() {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = entryId,
            Emoji = emoji,
        };
}
