using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class CallIdTest
{
    [Fact]
    public void TheSameConversationAlwaysProducesTheSameId()
    {
        // arrange
        var conversationId = NewConversationId(42);

        // act + assert
        CallId.For(conversationId).Should().Be(CallId.For(conversationId));
    }

    [Fact]
    public void DifferentConversationsProduceDifferentIds()
        => CallId.For(NewConversationId(42)).Should().NotBe(CallId.For(NewConversationId(43)));

    [Fact]
    public void TheIdIsStableAcrossProcesses()
    {
        // Pinning the value is the point: a push minted by the server and a call reported
        // by a restarted app have to agree without talking to each other. If this fails
        // after an intentional algorithm change, re-pin it - but the wire format changed,
        // so old clients will stop matching.
        CallId.For(NewConversationId(42)).ToString().Should().Be(ExpectedIdForConversation42);
    }

    [Fact]
    public void TheIdIsNotTheNilGuid()
        => CallId.For(NewConversationId(42)).Should().NotBe(Guid.Empty);

    // Private methods

    private static ConversationId NewConversationId(long lid)
        => ConversationId.New(ChatId.Parse("testchatid1234567890"), lid);

    private const string ExpectedIdForConversation42 = "3fd78e8d-a96a-f9b5-cdb4-7595bebed443";
}
