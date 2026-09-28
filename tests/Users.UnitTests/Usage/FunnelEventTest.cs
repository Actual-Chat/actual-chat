namespace ActualChat.Users.UnitTests.Usage;

public class FunnelEventTest
{
    [Theory]
    [InlineData(FunnelEvent.JoinUsed)]
    [InlineData(FunnelEvent.SignUp)]
    [InlineData(FunnelEvent.ContactsMatched)]
    [InlineData(FunnelEvent.SignInCompletedFromLink)]
    [InlineData((FunnelEvent)99)]
    public void ServerOnlyOrUnknownEventShouldNotBeClientReportable(FunnelEvent funnelEvent)
        => funnelEvent.IsClientReportable().Should().BeFalse();

    [Theory]
    [InlineData(FunnelEvent.JoinOpenedSignedOut)]
    [InlineData(FunnelEvent.InviteCopy)]
    [InlineData(FunnelEvent.ContactsAccessGranted)]
    [InlineData(FunnelEvent.InviteFindContacts)]
    public void ClientEventShouldBeClientReportable(FunnelEvent funnelEvent)
        => funnelEvent.IsClientReportable().Should().BeTrue();
}
