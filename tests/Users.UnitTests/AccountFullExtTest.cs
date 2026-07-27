namespace ActualChat.Users.UnitTests;

public class AccountFullExtTest
{
    [Fact]
    public async Task AllowsNullAccount()
    {
        // arrange
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);

        // act
        var action = () => accounts.Object.AssertCanRead(Session.New(), null, CancellationToken.None);

        // assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AllowsOwnAccount()
    {
        // arrange
        var session = Session.New();
        var ownAccount = new AccountFull(UserId.New()) { Status = AccountStatus.Active };
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);
        accounts.Setup(x => x.GetOwn(session, CancellationToken.None)).ReturnsAsync(ownAccount);

        // act
        var action = () => accounts.Object.AssertCanRead(session, ownAccount, CancellationToken.None);

        // assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AllowsAdmin()
    {
        // arrange
        var session = Session.New();
        var ownAccount = new AccountFull(UserId.New()) {
            Status = AccountStatus.Active,
            IsAdmin = true,
        };
        var accessedAccount = new AccountFull(UserId.New());
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);
        accounts.Setup(x => x.GetOwn(session, CancellationToken.None)).ReturnsAsync(ownAccount);

        // act
        var action = () => accounts.Object.AssertCanRead(session, accessedAccount, CancellationToken.None);

        // assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RejectsOtherAccount()
    {
        // arrange
        var session = Session.New();
        var ownAccount = new AccountFull(UserId.New()) { Status = AccountStatus.Active };
        var accessedAccount = new AccountFull(UserId.New());
        var accounts = new Mock<IAccounts>(MockBehavior.Strict);
        accounts.Setup(x => x.GetOwn(session, CancellationToken.None)).ReturnsAsync(ownAccount);

        // act
        var action = () => accounts.Object.AssertCanRead(session, accessedAccount, CancellationToken.None);

        // assert
        (await action.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("You can't read accounts of other users.");
    }

    [Theory]
    [InlineData("1-2187303414", "jjj.bbb@gmail.com", "tr/lL/pjB5kWvzY8dWhnQ45qCmAlrNGQE3Y6PrM75xk=", "KBHWN6oFNDXdTI6kkY7X7PaHM3AgJa+EoXUKV38bssU=")]
    [InlineData("91-6361751111", "aklqb6218bs1ekl@privaterelay.appleid.com", "nOycpTkso7wJPVX2oNlW/KvckJn1nZ0QQPb/j/ekAu4=", "gUg5zbI54zy/7wZCcdbw3rXH15gmfZhc+iCE6qI3J6g=")]
    public void ShouldCreateCorrectIdentities(string phone, string email, string expectedPhoneHash, string expectedEmailHash)
    {
        // act
        var emailId = ActualChat.Email.Parse(email);
        var account = new AccountFull("user1").WithPhoneIdentity(ActualChat.Phone.Parse(phone)).WithEmailIdentity(emailId);

        // assert
        account.Identities.Keys.Select(x => x.Id)
            .Should()
            .BeEquivalentTo(
                $"email/{email}",
                $"email-hash/{expectedEmailHash.Replace("/", "\\/")}",
                $"phone/{phone}",
                $"phone-hash/{expectedPhoneHash.Replace("/", "\\/")}");
    }
}
