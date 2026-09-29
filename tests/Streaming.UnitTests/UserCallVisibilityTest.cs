using ActualChat.Live;
using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class UserCallVisibilityTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private const string Phone = "phone-session";
    private const string Desktop = "desktop-session";

    [Fact]
    public void PlacedCallShouldBeOnlyOnThePlacingClient()
    {
        // arrange
        var call = Call(CallRole.Caller, CallPhase.Dialing, Phone, "phone-app");

        // act
        var isOnPhone = LiveSessions.IsOnClient(call, Phone, "phone-app");
        var isOnDesktop = LiveSessions.IsOnClient(call, Desktop, "desktop-tab");

        // assert
        isOnPhone.Should().BeTrue();
        isOnDesktop.Should().BeFalse("the desktop neither placed the call nor was rung for it (#4929)");
    }

    [Fact]
    public void AnsweredCallShouldBeOnlyOnTheAnsweringClient()
    {
        // arrange
        var call = Call(CallRole.Callee, CallPhase.Active, Phone, "phone-app");

        // act
        var isOnDesktop = LiveSessions.IsOnClient(call, Desktop, "desktop-tab");

        // assert
        isOnDesktop.Should().BeFalse();
    }

    [Fact]
    public void CallShouldNotBeOnAnotherTabOfTheSameBrowser()
    {
        // arrange — tabs share the session, but each runs its own client
        var call = Call(CallRole.Caller, CallPhase.Active, Desktop, "tab-1");

        // act
        var isOnOtherTab = LiveSessions.IsOnClient(call, Desktop, "tab-2");

        // assert
        isOnOtherTab.Should().BeFalse();
    }

    [Fact]
    public void RingShouldBeOnEveryClient()
    {
        // arrange — a ring the phone is answering right now still names the phone
        var call = Call(CallRole.Callee, CallPhase.Ringing, Phone, "phone-app");

        // act
        var isOnDesktop = LiveSessions.IsOnClient(call, Desktop, "desktop-tab");

        // assert
        isOnDesktop.Should().BeTrue("a ring is every client's to answer");
    }

    [Fact]
    public void ClaimNamingNoSessionShouldBeOnEveryClient()
    {
        // arrange — taken by a pod predating the owner fields
        var call = Call(CallRole.Caller, CallPhase.Active, null, null);

        // act
        var isOnDesktop = LiveSessions.IsOnClient(call, Desktop, "desktop-tab");

        // assert
        isOnDesktop.Should().BeTrue();
    }

    [Fact]
    public void CallOfAnOldClientShouldBeOnEveryTabOfItsSessionOnly()
    {
        // arrange — an old client sends no client id, so only its session is known
        var call = Call(CallRole.Caller, CallPhase.Active, Desktop, null);

        // act
        var isOnSameBrowser = LiveSessions.IsOnClient(call, Desktop, "desktop-tab");
        var isOnPhone = LiveSessions.IsOnClient(call, Phone, "phone-app");

        // assert
        isOnSameBrowser.Should().BeTrue();
        isOnPhone.Should().BeFalse();
    }

    [Fact]
    public void OldClientShouldSeeItsSessionCallOnly()
    {
        // arrange — an old client reads without a client id
        var call = Call(CallRole.Caller, CallPhase.Active, Phone, "phone-app");

        // act
        var isOnOldDesktop = LiveSessions.IsOnClient(call, Desktop, null);
        var isOnOldPhone = LiveSessions.IsOnClient(call, Phone, null);

        // assert
        isOnOldDesktop.Should().BeFalse();
        isOnOldPhone.Should().BeTrue();
    }

    private static UserCall Call(CallRole role, CallPhase phase, string? sessionHash, string? clientId)
        => new() {
            ChatId = ChatA,
            AuthorId = AuthorId.New(ChatA, 1),
            Role = role,
            Phase = phase,
            SessionHash = sessionHash,
            ClientId = clientId,
        };
}
