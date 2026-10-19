using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class CallControlsAvailabilityTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallControlsShouldRespectPeerPermissionsWithoutIncompleteUI(bool isContact)
    {
        // arrange
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var alice = await aliceTester.SignInAsUniqueAlice();
        var chatId = PeerChatId.New(bob.Id, alice.Id);
        await bobTester.CreateTextEntry(chatId, "hi");
        if (isContact)
            await aliceTester.CreateTextEntry(chatId, "hey");

        var hub = bobTester.ScopedAppServices.AppUIHub();
        var chat = await TestWait.When(async ct => {
            var current = await hub.Chats.Get(bobTester.Session, chatId, ct);
            current!.Rules.IsPeerContactPending.Should().Be(!isContact);
            return current;
        });
        var chatContext = new ChatContext(hub, chat);
        var aliceAuthor = await aliceTester.GetOwnAuthor(chatId);
        var entry = new CallEntry(ChatEntryId.New(chatId, 10)) {
            CallerId = aliceAuthor!.Id,
            Outcome = CallOutcome.NoAnswer,
            BeginsAt = Moment.Now,
        };
        bob.IsAdmin.Should().BeFalse();
        (await hub.Features.IsIncompleteUIEnabled(CancellationToken.None)).Should().BeFalse();

        // act
        var header = bobTester.Render<ChatHeaderCallButton>(p => p.AddCascadingValue(chatContext));
        var card = bobTester.Render<CallMessageView>(p => p
            .Add(x => x.ChatContext, chatContext)
            .Add(x => x.Entry, entry));

        // assert
        await TestWait.WhenRendered(header, () => {
            var button = header.Find(".btn-start-call");
            button.HasAttribute("disabled").Should().Be(!isContact);
        });
        card.FindAll(".call-message.c-action").Count.Should().Be(isContact ? 1 : 0);
    }
}
