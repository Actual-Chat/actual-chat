using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallUIRingTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Host = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Me = AuthorId.New(TestChatId, 2);

    [Fact]
    public void FindRingingCallShouldReturnMyRingingInvite()
    {
        // arrange
        var live = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing });

        // act
        var call = CallUI.FindRingingCall(live, Me);

        // assert
        call.Should().NotBeNull();
        call!.ChatId.Should().Be(TestChatId);
        call.Caller.Should().Be(Host);
    }

    [Fact]
    public void FindRingingCallShouldIgnoreNonRingingStates()
    {
        // arrange
        var statuses = new[] { CallInviteStatus.Accepted, CallInviteStatus.Declined, CallInviteStatus.Missed };

        // act
        var calls = statuses
            .Select(status => CallUI.FindRingingCall(NewCall(new CallInvite { InviteeId = Me, Status = status }), Me))
            .ToList();

        // assert
        calls.Should().AllSatisfy(call => call.Should().BeNull());
    }

    [Fact]
    public void FindRingingCallShouldIgnoreForeignInviteNullSessionNonCallAndOwnCall()
    {
        // arrange
        var other = AuthorId.New(TestChatId, 3);
        var foreignInvite = NewCall(new CallInvite { InviteeId = other, Status = CallInviteStatus.Ringing });
        var ambient = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing })
            with { Kind = LiveSessionKind.Ambient };
        // The caller is never invited, so their own call has no invite of theirs.
        var ownCall = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing });

        // act
        var calls = new[] {
            CallUI.FindRingingCall(null, Me),
            CallUI.FindRingingCall(foreignInvite, Me),
            CallUI.FindRingingCall(ambient, Me),
            CallUI.FindRingingCall(ownCall, Host),
        };

        // assert
        calls.Should().AllSatisfy(call => call.Should().BeNull());
    }

    [Fact]
    public void FindRingingCallShouldKeepRingingAfterSomeoneElseAnswered()
    {
        // arrange
        var other = AuthorId.New(TestChatId, 3);
        var live = NewCall(
                new CallInvite { InviteeId = other, Status = CallInviteStatus.Accepted },
                new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing })
            with { Conversation = new Conversation(ConversationId.New(TestChatId, 1)) };

        // act
        var call = CallUI.FindRingingCall(live, Me);

        // assert
        call.Should().NotBeNull("only my own invite decides whether a group call still rings me");
    }

    private static LiveSession NewCall(params CallInvite[] invites)
        => new() {
            ChatId = TestChatId,
            Host = Host,
            Kind = LiveSessionKind.Call,
            Invites = invites,
        };
}
