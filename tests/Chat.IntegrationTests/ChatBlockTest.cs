using ActualChat.Contacts;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatBlockTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task BlockerCannotPostInPeerChat()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        // Make sure both contacts exist by exchanging a greeting
        await aliceTester.CreateTextEntry(chatId, "hi");

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);

        // assert
        var post = () => aliceTester.CreateTextEntry(chatId, "still talking");
        await post.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BlockedUserCannotPostInPeerChat()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        await bobTester.CreateTextEntry(chatId, "hi");

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);

        // assert
        var post = () => bobTester.CreateTextEntry(chatId, "are you there");
        await post.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BlockedUserCannotEditOwnExistingMessage()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        var entry = await bobTester.CreateTextEntry(chatId, "hi");

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);

        // assert
        var edit = () => bobTester.UpdateTextEntry(entry.Id, "rewriting old message");
        await edit.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BlockedUserCanRemoveOwnExistingMessage()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        var entry = await bobTester.CreateTextEntry(chatId, "delete me later");

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);
        var remove = () => bobTester.RemoveTextEntry(entry.Id);

        // assert
        await remove.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BlockedPeerIsHiddenFromContactListAndShownInBlockedList()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        await aliceTester.CreateTextEntry(chatId, "hi");
        var contacts = aliceTester.AppServices.GetRequiredService<IContacts>();
        var bobContactId = ContactId.NewUser(alice.Id, bob.Id);

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);

        // assert
        await ComputedTest.When(async ct => {
            var ids = await contacts.ListIds(aliceTester.Session, null, ct);
            ids.Should().NotContain(bobContactId);

            var blocked = await contacts.ListBlockedIds(aliceTester.Session, ct);
            blocked.Should().Contain(bobContactId);
        });
    }

    [Fact]
    public async Task IsBlockedByPeerIsTrueForOtherSide()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        await bobTester.CreateTextEntry(chatId, "hi");
        var bobContacts = bobTester.AppServices.GetRequiredService<IContacts>();

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);

        // assert
        await ComputedTest.When(async ct => {
            var bobContactForAlice = await bobContacts.GetForChat(bobTester.Session, chatId, ct);
            bobContactForAlice.Should().NotBeNull();
            bobContactForAlice.IsBlocked.Should().BeFalse();
            bobContactForAlice.IsBlockedByPeer.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UnblockRestoresPosting()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        await aliceTester.CreateTextEntry(chatId, "hi");

        // act
        await BlockUser(aliceTester, alice.Id, bob.Id);
        var blockedPost = () => bobTester.CreateTextEntry(chatId, "blocked");
        await blockedPost.Should().ThrowAsync<InvalidOperationException>();
        await BlockUser(aliceTester, alice.Id, bob.Id, isBlocked: false);

        // assert
        var entry = await bobTester.CreateTextEntry(chatId, "back to talking");
        entry.Content.Should().Be("back to talking");
    }

    private static Task BlockUser(IWebTester tester, UserId ownerId, UserId otherUserId, bool isBlocked = true)
    {
        var contactId = ContactId.NewUser(ownerId, otherUserId);
        var cmd = new Contacts_SetIsBlocked(tester.Session, contactId, isBlocked);
        return tester.Commander.Call(cmd);
    }
}
