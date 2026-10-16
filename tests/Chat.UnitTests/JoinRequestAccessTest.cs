namespace ActualChat.Chat.UnitTests;

public sealed class JoinRequestAccessTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(JoinRequestAccess.Off, false, false, false)]
    [InlineData(JoinRequestAccess.Off, true, true, false)]
    [InlineData(JoinRequestAccess.PlaceMembers, true, false, false)]
    [InlineData(JoinRequestAccess.PlaceMembers, true, true, true)]
    [InlineData(JoinRequestAccess.SignedIn, false, false, false)]
    [InlineData(JoinRequestAccess.SignedIn, true, false, true)]
    [InlineData(JoinRequestAccess.Anyone, false, false, true)]
    public void AllowsShouldLetOnlyTheRightViewersIn(
        JoinRequestAccess access, bool isSignedIn, bool isPlaceMember, bool isExpected)
    {
        // act
        var isAllowed = access.Allows(isSignedIn, isPlaceMember);

        // assert
        isAllowed.Should().Be(isExpected);
    }

    [Theory]
    [InlineData(JoinRequestAccess.Anyone, JoinRequestAccess.Off, JoinRequestAccess.Off)]
    [InlineData(JoinRequestAccess.SignedIn, JoinRequestAccess.Anyone, JoinRequestAccess.SignedIn)]
    [InlineData(JoinRequestAccess.PlaceMembers, JoinRequestAccess.SignedIn, JoinRequestAccess.PlaceMembers)]
    public void MinShouldTakeTheStricterValue(
        JoinRequestAccess chat, JoinRequestAccess place, JoinRequestAccess expected)
    {
        // act
        var effective = chat.Min(place);

        // assert
        effective.Should().Be(expected);
    }
}
