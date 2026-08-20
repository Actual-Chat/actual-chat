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
        await commander.Call(new Reactions_React { Uuid = uuid, Session = session, Reaction = NewReaction(entryId, Emojis.Lol) });
        await commander.Call(new Reactions_React { Uuid = uuid, Session = session, Reaction = NewReaction(entryId, Emojis.Cool) });

        // assert — the duplicate never ran; the reaction stays Lol (Cool was not applied)
        await ComputedTest.When(async ct => {
            var reaction = await Reactions.Get(session, entryId, ct);
            reaction.Should().NotBeNull();
            reaction!.Emoji.Should().Be(Emojis.Lol);
        });

        // control — a fresh (auto-generated) Uuid does change the reaction to Cool
        await commander.Call(new Reactions_React { Session = session, Reaction = NewReaction(entryId, Emojis.Cool) });
        await ComputedTest.When(async ct => {
            var reaction = await Reactions.Get(session, entryId, ct);
            reaction!.Emoji.Should().Be(Emojis.Cool);
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
