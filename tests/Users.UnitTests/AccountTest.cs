namespace ActualChat.Users.UnitTests;

public class AccountTest
{
    [Fact]
    public void IsGuest_WithNullId_ShouldReturnTrue()
    {
        // arrange
        var account = new AccountFull("test-user");

        // act & assert
        account.IsGuest.Should().BeTrue();
    }

    [Fact]
    public void IsGuest_WithGuestId_ShouldReturnTrue()
    {
        // arrange
        var guestId = UserId.NewGuest();
        var account = new AccountFull(guestId);

        // act & assert
        account.IsGuest.Should().BeTrue();
    }

    [Fact]
    public void IsGuest_WithRegularId_ShouldReturnFalse()
    {
        // arrange
        var userId = UserId.New();
        var account = new AccountFull(userId);

        // act & assert
        account.IsGuest.Should().BeFalse();
    }

    [Fact]
    public void ToString_WithNullId_ShouldNotThrow()
    {
        // arrange - this constructor sets Id to null
        var account = new AccountFull("test-user");

        // act & assert - ToString calls PrintMembers which accesses IsGuest
        var action = () => account.ToString();
        action.Should().NotThrow();
    }
}
