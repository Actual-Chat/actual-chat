using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class OutgoingCallScreensTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Bob => field ??= AppHost.NewBlazorTester(Out);
    private BlazorTester Alice => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Bob.DisposeSilentlyAsync();
        await Alice.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task CallingOneInviteeShouldNameThemBeforeTheServerAnswers()
    {
        // arrange
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var hub = Bob.ScopedAppServices.AppUIHub();

        // act - the claim the screens read is taken before the StartCall round trip
        hub.CallUI.TryClaimOutgoing(chatId, aliceAuthor!.Id, false).Should().BeTrue();

        // assert - the peer comes from the gesture, so no session read is needed to name them
        (await hub.CallScreensUI.GetCallPeerId(CancellationToken.None)).Should().Be(aliceAuthor.Id);
        // ... and the screen goes up on that alone
        (await hub.CallScreensUI.GetCallView(CancellationToken.None)).Kind
            .Should().NotBe(CallViewKind.None);
        // ... while the ringback still waits for the server to name this call as mine
        (await hub.CallUI.GetDialingOutChatId(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task TheRingbackShouldWaitForTheServerToConfirm()
    {
        // arrange - CallUI.StartCall can't run here: it asks for the microphone first, and there is no
        // browser to answer that, so the gesture and the server's side are driven apart
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var hub = Bob.ScopedAppServices.AppUIHub();

        // act - the claim alone must not open the gate
        hub.CallUI.TryClaimOutgoing(chatId, aliceAuthor!.Id, false).Should().BeTrue();
        (await hub.CallUI.GetDialingOutChatId(CancellationToken.None)).Should().BeNull();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);

        // assert - the ringback gate opens on its own, from the server answer rather than the gesture
        await ComputedTest.When(async ct => {
            (await hub.CallUI.GetDialingOutChatId(ct)).Should().Be(chatId);
        }, TimeSpan.FromSeconds(10));
    }
}
