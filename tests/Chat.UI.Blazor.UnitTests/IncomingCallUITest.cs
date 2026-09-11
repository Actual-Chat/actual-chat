using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class IncomingCallUITest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Host = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Me = AuthorId.New(TestChatId, 2);

    [Fact]
    public void FindsMyRingingInvite()
    {
        var live = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing });

        var call = IncomingCallUI.FindRingingCall(live, Me);

        call.Should().NotBeNull();
        call!.ChatId.Should().Be(TestChatId);
        call.Caller.Should().Be(Host);
    }

    [Fact]
    public void IgnoresNonRingingStates()
    {
        foreach (var status in new[] { CallInviteStatus.Accepted, CallInviteStatus.Declined, CallInviteStatus.Missed }) {
            var live = NewCall(new CallInvite { InviteeId = Me, Status = status });
            IncomingCallUI.FindRingingCall(live, Me).Should().BeNull();
        }
    }

    [Fact]
    public void IgnoresForeignInviteNullSessionNonCallAndOwnCall()
    {
        var other = AuthorId.New(TestChatId, 3);
        IncomingCallUI.FindRingingCall(null, Me).Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = other, Status = CallInviteStatus.Ringing }), Me)
            .Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing })
                with { Kind = LiveSessionKind.Ambient },
            Me)
            .Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = Host, Status = CallInviteStatus.Ringing }), Host)
            .Should().BeNull();
    }

    private static LiveSession NewCall(params CallInvite[] invites)
        => new() {
            ChatId = TestChatId,
            Host = Host,
            Kind = LiveSessionKind.Call,
            Invites = invites,
        };
}
