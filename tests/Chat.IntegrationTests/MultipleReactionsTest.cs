using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class MultipleReactionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IReactions Reactions => field ??= Tester.AppServices.GetRequiredService<IReactions>();
    private IChatsBackend ChatsBackend => field ??= Tester.AppServices.GetRequiredService<IChatsBackend>();
    private DbHub<ChatDbContext> DbHub => field ??= Tester.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task AuthorShouldKeepSeveralReactionsOnOneEntry()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "react twice");

        // act
        await Tester.React(entry.Id, Emojis.ThumbsUp);
        await Tester.React(entry.Id, Emojis.Lol);

        // assert
        await WaitForReactions(entry.Id, [Emojis.ThumbsUp, Emojis.Lol]);
        var reacted = await ChatsBackend.GetEntry(entry.Id, default);
        reacted!.HasReactions.Should().BeTrue();

        // act — each emoji toggles off on its own
        await Tester.React(entry.Id, Emojis.ThumbsUp);

        // assert
        await WaitForReactions(entry.Id, [Emojis.Lol]);
        var stillReacted = await ChatsBackend.GetEntry(entry.Id, default);
        stillReacted!.HasReactions.Should().BeTrue("the author's other reaction is still there");

        // act
        await Tester.React(entry.Id, Emojis.Lol);

        // assert
        await WaitForReactions(entry.Id, []);
        await TestWait.When(async ct => {
            var cleared = await ChatsBackend.GetEntry(entry.Id, ct);
            cleared!.HasReactions.Should().BeFalse();
        });
    }

    [Fact]
    public async Task LegacyReactionShouldToggleOffAndCoexistWithNewOnes()
    {
        // arrange — a reaction stored under the pre-5504 "{entryId}:{authorId}" id
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chatId, "legacy reaction");
        await Tester.React(entry.Id, Emojis.ThumbsUp);
        await WaitForReactions(entry.Id, [Emojis.ThumbsUp]);
        var authorId = (await Reactions.ListOwn(Tester.Session, entry.Id, default)).Single().AuthorId;
        var legacyId = $"{entry.Id}:{authorId}";
        await using (var dbContext = await DbHub.CreateDbContext(true)) {
            await dbContext.Reactions
                .Where(x => x.EntryId == entry.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Id, legacyId));
        }

        // act
        await Tester.React(entry.Id, Emojis.Lol);
        await Tester.React(entry.Id, Emojis.ThumbsUp);

        // assert
        await WaitForReactions(entry.Id, [Emojis.Lol]);
        await using var verifyContext = await DbHub.CreateDbContext();
        var ids = await verifyContext.Reactions
            .Where(x => x.EntryId == entry.Id.Value)
            .Select(x => x.Id)
            .ToListAsync();
        ids.Should().Equal([$"{entry.Id}:{authorId}:{Emojis.Lol.Value}"], "the legacy row was the one toggled off");
    }

    // Private methods

    private Task WaitForReactions(ChatEntryId entryId, Emoji[] expected)
        => TestWait.When(async ct => {
            var own = await Reactions.ListOwn(Tester.Session, entryId, ct);
            own.Select(x => x.Emoji).Should().BeEquivalentTo(expected);
            var summaries = await Reactions.ListSummaries(Tester.Session, entryId, ct);
            summaries.Select(x => (x.Emoji, x.Count)).Should().BeEquivalentTo(expected.Select(x => (x, 1L)));
        });
}
