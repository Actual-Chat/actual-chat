using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class IncomingCallAcceptTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task AcceptShouldJoinARingTheClientHasNotReadYet()
    {
        // arrange - a client woken by the call push reads "no call" until its first real answer
        // lands, so the answer from the notification can come first. Accept doesn't read the
        // session at all any more: the server decides, and it knows before the push goes out.
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var callScreensUI = Alice.ScopedAppServices.AppUIHub().CallScreensUI;

        // act - the ring exists server-side; Alice answers without her client having read it
        // (the join itself can fail in a test host - there is no audio there)
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await callScreensUI.Accept(chatId).SilentAwait();

        // assert - the ring was answered rather than called over
        await ComputedTest.When(async ct => {
            var live = await backend.Get(chatId, ct);
            var invite = live?.Invites.FirstOrDefault(x => x.InviteeId == aliceAuthor.Id);
            invite.Should().NotBeNull();
            invite!.Status.Should().Be(CallInviteStatus.Accepted);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AcceptShouldNotFailWhenTheCallIsAlreadyOurs()
    {
        // arrange - an RPC resend after a reconnect, a second tap on the notification, and the server's
        // own Ringing -> Active promotion all reach the server as a second answer
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // act
        var again = async () => await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert - the same answer, not an error: CallScreensUI.Accept hangs the call up on a throw
        await again.Should().NotThrowAsync();
        var live = await backend.Get(chatId, default);
        live!.Invites.Single(x => x.InviteeId == aliceAuthor.Id)
            .Status.Should().BeOneOf(CallInviteStatus.Accepted, CallInviteStatus.Active);
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptShouldRejectARingThatIsNotThere()
    {
        // arrange - the tap a stale notification produces: nothing is ringing in this chat
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        var accept = async () => await backend.AcceptCall(chatId, aliceAuthor!.Id, default);

        // assert - it says so rather than reporting success and leaving the caller in a call that
        // never was; CallScreensUI.Accept releases the slot on exactly this.
        await accept.Should().ThrowAsync<Exception>();
        var live = await backend.Get(chatId, default);
        live?.Invites.FirstOrDefault(x => x.InviteeId == aliceAuthor!.Id).Should().BeNull();
    }
}
