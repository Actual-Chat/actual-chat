using ActualChat.Contacts;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class NonContactPeerLimitTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task RulesShouldStripContentFlagsForNonContactPeer()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var chats = aliceTester.AppServices.GetRequiredService<IChats>();

        // act
        var rules = await chats.GetRules(aliceTester.Session, peerChatId, CancellationToken.None);

        // assert
        rules.CanRead().Should().BeTrue();
        rules.CanWrite().Should().BeTrue();
        rules.CanUpload().Should().BeFalse();
        rules.CanWriteAudio().Should().BeFalse();
        rules.CanWriteVideo().Should().BeFalse();
        rules.CanReadAudio().Should().BeFalse();
        rules.CanReadVideo().Should().BeFalse();
    }

    [Fact]
    public async Task RulesShouldGrantContentFlagsWhenSenderIsInRecipientContacts()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();

        // Bob adds Alice to his contacts - restrictions on Alice should lift.
        await bobTester.CreatePeerContact(bob, alice);

        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var chats = aliceTester.AppServices.GetRequiredService<IChats>();

        // act
        var rules = await chats.GetRules(aliceTester.Session, peerChatId, CancellationToken.None);

        // assert
        rules.CanWrite().Should().BeTrue();
        rules.CanUpload().Should().BeTrue();
        rules.CanWriteAudio().Should().BeTrue();
        rules.CanWriteVideo().Should().BeTrue();
        rules.CanReadAudio().Should().BeTrue();
        rules.CanReadVideo().Should().BeTrue();
    }

    [Fact]
    public async Task RulesShouldGrantContentFlagsAfterRecipientReplies()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var chats = aliceTester.AppServices.GetRequiredService<IChats>();

        // Alice sends a message; Bob (the recipient) replies.
        await aliceTester.CreateTextEntry(peerChatId, "Hi");
        await bobTester.CreateTextEntry(peerChatId, "Hi back");

        // act, assert - Alice's restrictions should have lifted.
        await ComputedTest.When(async ct => {
            var rules = await chats.GetRules(aliceTester.Session, peerChatId, ct);
            rules.CanUpload().Should().BeTrue();
            rules.CanWriteAudio().Should().BeTrue();
            rules.CanWriteVideo().Should().BeTrue();
            rules.CanReadAudio().Should().BeTrue();
            rules.CanReadVideo().Should().BeTrue();
        });
    }

    [Fact]
    public async Task ShouldCapNonContactSenderAtMessageLimit()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var cap = Constants.Chat.NonContactPeerMessageLimit;

        // act - Alice posts up to the cap successfully.
        for (var i = 0; i < cap; i++)
            await aliceTester.CreateTextEntry(peerChatId, $"msg {i + 1}");

        // assert - the next create must fail.
        var send = () => aliceTester.CreateTextEntry(peerChatId, "should be blocked");
        await send.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task EditsShouldRemainAllowedAtCap()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var cap = Constants.Chat.NonContactPeerMessageLimit;

        var entries = new List<ChatEntry>();
        for (var i = 0; i < cap; i++)
            entries.Add(await aliceTester.CreateTextEntry(peerChatId, $"msg {i + 1}"));

        // act - editing the first entry should still succeed even though the cap is reached.
        var firstEntryId = ChatEntryId.New(peerChatId, entries[0].LocalId);
        var edited = await aliceTester.UpdateTextEntry(firstEntryId, "edited!");

        // assert
        edited.Content.Should().Be("edited!");
    }

    [Fact]
    public async Task CapShouldLiftAfterRecipientReplies()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var cap = Constants.Chat.NonContactPeerMessageLimit;

        for (var i = 0; i < cap; i++)
            await aliceTester.CreateTextEntry(peerChatId, $"msg {i + 1}");

        // Recipient replies - restrictions should lift.
        await bobTester.CreateTextEntry(peerChatId, "Replying");

        // act, assert - Alice can now post freely. The rules read is both the gate the cap check
        // uses and the dependency ComputedTest.When retries on - a lone command captures none.
        await ComputedTest.When(async ct => {
            var peerChat = await aliceTester.Chats.Get(aliceTester.Session, peerChatId, ct);
            peerChat!.Rules.Has(ChatPermissions.WriteAudio).Should().BeTrue();

            var entry = await aliceTester.CreateTextEntry(peerChatId, "post-reply message");
            entry.Content.Should().Be("post-reply message");
        });
    }

    [Fact]
    public async Task SenderMessageShouldNotStoreRecipientContact()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var contactsBackend = aliceTester.AppServices.GetRequiredService<IContactsBackend>();

        // act
        await aliceTester.CreateTextEntry(peerChatId, "Hi");

        // assert
        var aliceContact = await contactsBackend.Get(alice.Id, ContactId.NewUser(alice.Id, bob.Id), CancellationToken.None);
        aliceContact.State.Should().Be(ContactState.Regular);
        var bobContact = await contactsBackend.Get(bob.Id, ContactId.NewUser(bob.Id, alice.Id), CancellationToken.None);
        bobContact.State.Should().Be(ContactState.Temporary);
    }

    [Fact]
    public async Task RecipientAddToContactsShouldPromoteTemporaryAndLiftCap()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var contactsBackend = aliceTester.AppServices.GetRequiredService<IContactsBackend>();
        var cap = Constants.Chat.NonContactPeerMessageLimit;
        for (var i = 0; i < cap; i++)
            await aliceTester.CreateTextEntry(peerChatId, $"msg {i + 1}");

        // Sanity: Bob's auto-created contact for Alice is Temporary, so the banner is visible to Bob.
        var bobContactBefore = await contactsBackend.Get(bob.Id, ContactId.NewUser(bob.Id, alice.Id), CancellationToken.None);
        bobContactBefore.HasVersion().Should().BeTrue("a Temporary contact still has Version > 0");
        bobContactBefore.IsRegular.Should().BeFalse("Temporary contacts must not look 'stored' to the banner");

        // act - Bob clicks "Add to contacts" (same path as AddToContactsBanner.OnAddClick).
        await bobTester.CreatePeerContact(bob, alice);

        // assert - Bob's contact promotes to Regular, banner is now hidden, and Alice's cap lifts.
        var bobContactAfter = await contactsBackend.Get(bob.Id, ContactId.NewUser(bob.Id, alice.Id), CancellationToken.None);
        bobContactAfter.IsRegular.Should().BeTrue();
        // The rules read is the cap check's gate - see the sibling test.
        await ComputedTest.When(async ct => {
            var peerChat = await aliceTester.Chats.Get(aliceTester.Session, peerChatId, ct);
            peerChat!.Rules.Has(ChatPermissions.WriteAudio).Should().BeTrue();

            var entry = await aliceTester.CreateTextEntry(peerChatId, "post-add message");
            entry.Content.Should().Be("post-add message");
        });
    }

    [Fact]
    public async Task ParallelSendsShouldNotExceedCap()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var cap = Constants.Chat.NonContactPeerMessageLimit;
        var attempts = cap * 4;

        // act - fire many concurrent sends from Alice and collect outcomes.
        var sends = Enumerable.Range(0, attempts)
            .Select(i => Task.Run(async () => {
                try {
                    await aliceTester.CreateTextEntry(peerChatId, $"race {i}").ConfigureAwait(false);
                    return true;
                }
                catch {
                    return false;
                }
            }))
            .ToArray();
        var results = await Task.WhenAll(sends);
        var successes = results.Count(r => r);

        // assert - the cap holds even under concurrent sends.
        successes.Should().BeLessThanOrEqualTo(cap);

        var chats = aliceTester.AppServices.GetRequiredService<IChats>();
        var range = await chats.GetIdRange(aliceTester.Session, peerChatId, CancellationToken.None);
        (range.End - range.Start).Should().BeLessThanOrEqualTo(cap);
    }

    [Fact]
    public async Task ThreadStartShouldBeBlockedForNonContactPeer()
    {
        // arrange
        var appHost = AppHost;
        await using var aliceTester = appHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = appHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var entry = await aliceTester.CreateTextEntry(peerChatId, "Hi");

        // act, assert
        var command = new ChatThreads_Start {
            Session = aliceTester.Session,
            ParentChatId = peerChatId,
            Title = "Thread",
            Description = "",
            EntryIds = [entry.Id],
        };
        var startThread = () => aliceTester.Commander.Call(command);
        await startThread.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GroupChatShouldNotBeAffectedByNonContactCap()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsBob();
        var (chatId, _) = await tester.CreateChat(true);

        // act - post more than the peer-chat cap; group chat must permit it.
        for (var i = 0; i < Constants.Chat.NonContactPeerMessageLimit + 1; i++)
            await tester.CreateTextEntry(chatId, $"msg {i + 1}");

        // assert - last post observable.
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var rules = await chats.GetRules(tester.Session, chatId, CancellationToken.None);
        rules.CanUpload().Should().BeTrue();
        rules.CanWriteAudio().Should().BeTrue();
        rules.CanWriteVideo().Should().BeTrue();
    }
}
