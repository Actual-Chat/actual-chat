namespace ActualChat.Mui.UnitTests;

public class MuiAccessTest
{
    [Fact]
    public void MissingAccountIsSignedOut()
    {
        // arrange
        AccountFull? account = null;

        // act
        var access = account.GetMuiAccess();

        // assert
        access.Should().Be(MuiAccess.SignedOut);
    }

    [Fact]
    public void GuestAccountIsSignedOut()
    {
        // arrange
        var account = new AccountFull(UserId.NewGuest());

        // act
        var access = account.GetMuiAccess();

        // assert
        access.Should().Be(MuiAccess.SignedOut);
    }

    [Fact]
    public void RegularAccountIsNotAdmin()
    {
        // arrange
        var account = new AccountFull(UserId.New());

        // act
        var access = account.GetMuiAccess();

        // assert
        access.Should().Be(MuiAccess.Denied);
    }

    [Fact]
    public void AdminAccountIsAdmin()
    {
        // arrange
        var account = new AccountFull(UserId.New()) { IsAdmin = true };

        // act
        var access = account.GetMuiAccess();

        // assert
        access.Should().Be(MuiAccess.Granted);
    }
}
