using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class AccountTombstoneTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend Backend => field ??= Owner.AppServices.GetRequiredService<IChatsBackend>();
    private IAccountsBackend Accounts => field ??= Owner.AppServices.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend Authors => field ??= Owner.AppServices.GetRequiredService<IAuthorsBackend>();
    private DbHub<ChatDbContext> DbHub => field ??= Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task RemovedAccountShouldHideItsMessagesAtOnce()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var kept = await Owner.CreateTextEntry(chatId, "keep");
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        var removed = await member.CreateTextEntry(chatId, "goes away");
        var range = Constants.Chat.EntryIdTiles.GetTile(removed.LocalId).Range;
        var tile = await Computed.Capture(() => Backend.GetTile(chatId, range, true, default));
        var authorId = removed.AuthorId;

        // act
        await Owner.Commander.Call(new AccountsBackend_Delete(bob.Id));

        // assert
        (await Accounts.Get(bob.Id, default)).Should().BeNull();
        // Exists is consolidated, so it and everything downstream of it - the author, the tiles -
        // follow within its consolidation delay rather than in the same instant.
        await TestWait.WhenPolled(async () => {
            (await Accounts.Exists(bob.Id, default)).Should().BeFalse();
            (await Authors.Get(chatId, authorId, RequestedAuthorKind.Full, default)).Should().BeNull();
            (await Authors.Exists(chatId, authorId, default)).Should().BeFalse();
            tile.IsConsistent().Should().BeFalse();
            (await Backend.GetEntry(removed.Id, default)).Should().BeNull();
        }, TimeSpan.FromSeconds(10));
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("keep");
    }

    [Fact]
    public async Task RemovedAccountShouldNotBeSignedInAgain()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        await using var member = AppHost.NewWebClientTester(Out);
        var identity = $"bob-{Ulid.NewUlid()}";
        var bob = await member.SignInAsBob(identity);

        // act
        await Owner.Commander.Call(new AccountsBackend_Delete(bob.Id));

        // assert
        // The row survives as a tombstone, but nothing can reach it: its identities are gone, so a
        // returning user gets a fresh id rather than this one.
        (await Accounts.Get(bob.Id, default)).Should().BeNull();
        await using var next = AppHost.NewWebClientTester(Out);
        var bobAgain = await next.SignInAsBob(identity);
        bobAgain.Id.Should().NotBe(bob.Id);
    }

    [Fact]
    public async Task RemovedAccountEntriesShouldBePurgedBeforeItsAuthorsGo()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var kept = await Owner.CreateTextEntry(chatId, "keep");
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        var removed = await member.CreateTextEntry(chatId, "goes away");

        // act
        // The order the account-removal path uses: the account goes first, the entries drain after,
        // and each chat's author - which is how its entries are found - goes once they are purged.
        await Owner.Commander.Call(new AccountsBackend_Delete(bob.Id));
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id));

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.ChatEntries.AnyAsync(e => e.Id == removed.Id.Value && !e.IsRemovedAndPurged))
                .Should().BeFalse();
            (await db.Authors.AnyAsync(a => a.UserId == bob.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("keep");
    }
}
