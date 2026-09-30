using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class CallUuidTest
{
    private const string ExpectedIdForCall42 = "3fd78e8d-a96a-f9b5-cdb4-7595bebed443";

    [Fact]
    public void TheSameCallShouldAlwaysProduceTheSameId()
    {
        // arrange
        var callId = NewCallId(42);

        // act + assert
        CallUuid.For(callId).Should().Be(CallUuid.For(callId));
    }

    [Fact]
    public void DifferentCallsShouldProduceDifferentIds()
        => CallUuid.For(NewCallId(42)).Should().NotBe(CallUuid.For(NewCallId(43)));

    [Fact]
    public void TheIdShouldBeStableAcrossProcesses()
    {
        // Pinning the value is the point: a push minted by the server and a call reported
        // by a restarted app have to agree without talking to each other. If this fails
        // after an intentional algorithm change, re-pin it - but the wire format changed,
        // so clients built before the change will stop matching.
        // act + assert
        CallUuid.For(NewCallId(42)).ToString().Should().Be(ExpectedIdForCall42);
    }

    [Fact]
    public void TheIdShouldNotBeTheNilGuid()
        => CallUuid.For(NewCallId(42)).Should().NotBe(Guid.Empty);

    // Private methods

    private static CallId NewCallId(long localId)
        => CallId.New(ChatId.Parse("testchatid1234567890"), localId.ToString());
}
