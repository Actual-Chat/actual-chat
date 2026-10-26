using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ReactionIntentTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IReactions Reactions => Tester.AppServices.GetRequiredService<IReactions>();

    [Fact]
    public async Task StatedIntentShouldNotToggleAnExistingOutcome()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chat, _) = await Tester.CreateAndGetChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chat.Id, "Hello");
        var entryId = entry.Id;
        var session = Tester.Session;

        // act
        await React(entryId, Emojis.Lol, mustRemove: false);
        await React(entryId, Emojis.Lol, mustRemove: false);

        // assert
        await TestWait.When(async ct => {
            var reaction = await Reactions.Get(session, entryId, ct);
            reaction.Should().NotBeNull(because: "a repeated add is a no-op, not a toggle");
            reaction!.Emoji.Should().Be(Emojis.Lol);
        });

        // act
        await React(entryId, Emojis.Cool, mustRemove: true);

        // assert
        await TestWait.When(async ct => {
            var reaction = await Reactions.Get(session, entryId, ct);
            reaction!.Emoji.Should().Be(Emojis.Lol,
                because: "removing an emoji the author doesn't have changes nothing");
        });

        // act
        await React(entryId, Emojis.Lol, mustRemove: true);
        await React(entryId, Emojis.Lol, mustRemove: true);

        // assert
        await TestWait.When(async ct => {
            var reaction = await Reactions.Get(session, entryId, ct);
            reaction.Should().BeNull(because: "a repeated removal is a no-op, not a toggle");
        });
    }

    // Private methods

    private Task React(ChatEntryId entryId, Emoji emoji, bool mustRemove)
    {
        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = entryId,
            Emoji = emoji,
        };
        var reactCmd = new Reactions_React { Session = Tester.Session, Reaction = reaction, MustRemove = mustRemove };
        return Tester.Commander.Call(reactCmd);
    }
}
