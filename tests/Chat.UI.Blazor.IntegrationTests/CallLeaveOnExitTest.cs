using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class CallLeaveOnExitTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task LeavingOnExitShouldDropThePresence()
    {
        // arrange - Bob listens in the chat, which is what the server keeps him in a call by
        await Bob.SignInAsUniqueBob();
        var (chatId, _) = await Bob.CreateChat(false);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var hub = Bob.ScopedAppServices.AppUIHub();
        // Resolving it starts its participation sync, as the app's service starter does
        _ = hub.LiveSessionUI;
        await hub.ChatAudioUI.SetListeningState(chatId, true);
        await TestWait.When(async ct => {
            var participants = await backend.ListParticipants(chatId, ct);
            participants.Should().Contain(bobAuthor!.Id);
        });

        // act
        await hub.CallUI.LeaveOnExit(CancellationToken.None);

        // assert - the leave is answered by now; the wait is for ListParticipants' own consolidation delay
        await TestWait.When(async ct => {
            var participantsLeft = await backend.ListParticipants(chatId, ct);
            participantsLeft.Should().NotContain(bobAuthor!.Id);
        });
    }

    [Fact]
    public async Task LeavingOnExitShouldCancelADialingCall()
    {
        // arrange - Bob rings Alice; the server's answer names the call in the slot
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var hub = Bob.ScopedAppServices.AppUIHub();
        hub.CallUI.TryClaimOutgoing(chatId, aliceAuthor!.Id, false).Should().BeTrue();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);
        await TestWait.When(async ct => {
            var dialingChatId = await hub.CallUI.GetDialingOutChatId(ct);
            dialingChatId.Should().Be(chatId);
        });

        // act
        await hub.CallUI.LeaveOnExit(CancellationToken.None);

        // assert - Alice's ring is over, as it is after a hang-up while dialing
        var callState = await backend.GetCallState(chatId, default);
        callState.Should().BeNull();
        hub.CallUI.GetActiveCallNonComputed().Should().BeNull();
    }
}
