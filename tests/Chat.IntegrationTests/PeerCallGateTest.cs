using ActualChat.Contacts;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class PeerCallGateTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PeerCallToNonContactIsRejected()
    {
        // arrange — Bob reaches out to Alice (allowed up to the non-contact message cap), which does
        // not make him a stored contact of hers.
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var alice = await aliceTester.SignInAsUniqueAlice();
        var chatId = PeerChatId.New(bob.Id, alice.Id);
        await bobTester.CreateTextEntry(chatId, "hi");
        var liveSessions = bobTester.AppServices.GetRequiredService<ILiveSessions>();

        // act + assert — Alice hasn't added Bob nor replied, so a call to her is refused.
        var startCall = () => liveSessions.StartCall(bobTester.Session, chatId, default, false, default);
        await startCall.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PeerCallAllowedAfterRecipientReplies()
    {
        // arrange — Bob greets Alice and Alice replies, which stores Bob as a regular contact.
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var alice = await aliceTester.SignInAsUniqueAlice();
        var chatId = PeerChatId.New(bob.Id, alice.Id);
        await bobTester.CreateTextEntry(chatId, "hi");
        await aliceTester.CreateTextEntry(chatId, "hey");
        var chats = bobTester.AppServices.GetRequiredService<IChats>();
        var liveSessions = bobTester.AppServices.GetRequiredService<ILiveSessions>();

        // The reply stores the contact via an event, so wait for the call gate to lift.
        await ComputedTest.When(async ct => {
            var chat = await chats.Get(bobTester.Session, chatId, ct);
            chat!.Rules.CanWriteAudio().Should().BeTrue();
        }, TimeSpan.FromSeconds(10));

        // act + assert — the call now goes through.
        var aliceAuthor = await aliceTester.GetOwnAuthor(chatId);
        var startCall = () => liveSessions.StartCall(
            bobTester.Session, chatId, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await startCall.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PeerCallToBlockedPeerIsRejected()
    {
        // arrange — Bob and Alice can normally call each other (she replied), then Alice blocks Bob.
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var alice = await aliceTester.SignInAsUniqueAlice();
        var chatId = PeerChatId.New(bob.Id, alice.Id);
        await bobTester.CreateTextEntry(chatId, "hi");
        await aliceTester.CreateTextEntry(chatId, "hey");
        var chats = bobTester.AppServices.GetRequiredService<IChats>();
        var liveSessions = bobTester.AppServices.GetRequiredService<ILiveSessions>();
        await ComputedTest.When(async ct => {
            var chat = await chats.Get(bobTester.Session, chatId, ct);
            chat!.Rules.CanWriteAudio().Should().BeTrue();
        }, TimeSpan.FromSeconds(10));

        // act — Alice blocks Bob; the block strips his call permission again.
        var blockContactId = ContactId.NewUser(alice.Id, bob.Id);
        await aliceTester.Commander.Call(new Contacts_SetIsBlocked(aliceTester.Session, blockContactId, true));
        await ComputedTest.When(async ct => {
            var chat = await chats.Get(bobTester.Session, chatId, ct);
            chat!.Rules.CanWriteAudio().Should().BeFalse();
        }, TimeSpan.FromSeconds(10));

        // assert — the call to the blocking peer is refused.
        var startCall = () => liveSessions.StartCall(bobTester.Session, chatId, default, false, default);
        await startCall.Should().ThrowAsync<InvalidOperationException>();
    }
}
