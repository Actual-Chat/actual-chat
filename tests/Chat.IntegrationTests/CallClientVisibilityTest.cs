using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallClientVisibilityTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task PlacedCallShouldShowOnlyOnThePlacingClient()
    {
        // arrange
        await using var bobPhone = AppHost.NewBlazorTester(Out);
        await using var bobDesktop = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var (chatId, _, alice) = await NewPeerChat(bobPhone, bobDesktop, aliceTester);
        var liveSessions = bobPhone.AppServices.GetRequiredService<ILiveSessions>();

        // act
        var callId = await liveSessions.StartCall(
            bobPhone.Session, chatId, new[] { alice.Id }.ToApiArray(), false, "phone", default);

        // assert
        callId.Should().NotBeNull();
        await TestWait.When(async ct => {
            var onPhone = await liveSessions.GetMyCall(bobPhone.Session, "phone", ct);
            onPhone.Should().NotBeNull();
            onPhone!.Role.Should().Be(CallRole.Caller);
            onPhone.CallId.Should().Be(callId, "StartCall names the call the caller is then shown");
        });
        var onDesktop = await liveSessions.GetMyCall(bobDesktop.Session, "desktop", default);
        var onPhoneOtherTab = await liveSessions.GetMyCall(bobPhone.Session, "phone-tab-2", default);
        onDesktop.Should().BeNull("the desktop neither placed the call nor was rung for it (#4929)");
        onPhoneOtherTab.Should().BeNull("another tab of the same browser is another client");
        await TestWait.When(async ct => {
            var ring = await liveSessions.GetMyCall(aliceTester.Session, "alice-any", ct);
            ring.Should().NotBeNull();
            ring!.Phase.Should().Be(CallPhase.Ringing, "a ring is every client's to answer");
        });
    }

    [Fact]
    public async Task AnsweredCallShouldShowOnlyOnTheAnsweringClient()
    {
        // arrange
        await using var bobPhone = AppHost.NewBlazorTester(Out);
        await using var bobDesktop = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var (chatId, _, alice) = await NewPeerChat(bobPhone, bobDesktop, aliceTester);
        var liveSessions = bobPhone.AppServices.GetRequiredService<ILiveSessions>();
        var callId = await liveSessions.StartCall(
            bobPhone.Session, chatId, new[] { alice.Id }.ToApiArray(), false, "phone", default);
        await TestWait.When(async ct => {
            var ring = await liveSessions.GetMyCall(aliceTester.Session, "alice-phone", ct);
            ring.Should().NotBeNull();
        });

        // act
        await liveSessions.AcceptCall(aliceTester.Session, callId, "alice-phone", default);

        // assert
        await TestWait.When(async ct => {
            var onOtherClient = await liveSessions.GetMyCall(aliceTester.Session, "alice-laptop", ct);
            onOtherClient.Should().BeNull("the call was answered on another client");
        });
        var onAnsweringClient = await liveSessions.GetMyCall(aliceTester.Session, "alice-phone", default);
        onAnsweringClient.Should().NotBeNull();
        onAnsweringClient!.Phase.Should().Be(CallPhase.Active);
    }

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(
        IWebTester bobPhone, IWebTester bobDesktop, IWebTester aliceTester)
    {
        var bob = await bobPhone.SignInAsUniqueBob();
        await bobDesktop.SignIn(bob);
        var alice = await aliceTester.SignInAsUniqueAlice();
        // Alice adding Bob lets him call her - the peer-call gate.
        await aliceTester.CreatePeerContact(alice, bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = bobPhone.AppServices.GetRequiredService<IAuthorsBackend>();
        return (chatId,
            await authors.EnsureJoined(chatId, bob.Id, default),
            await authors.EnsureJoined(chatId, alice.Id, default));
    }
}
