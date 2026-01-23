namespace ActualChat.Users.UnitTests;

public class AccountSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void AccountSerializationTest_Basic()
    {
        var userId = UserId.New();
        var avatar = new Avatar("avatar-1", 1) {
            Name = "Test Avatar",
            Bio = "Test Bio",
            AvatarKey = "key-1",
        };
        var account = new Account(userId, 42) {
            Status = AccountStatus.Active,
            Avatar = avatar,
        };

        var s = account.PassThroughAllSerializers(Out);

        AssertAccountEqual(s, account);
    }

    [Fact]
    public void AccountSerializationTest_WithGuest()
    {
        var guestId = UserId.NewGuest();
        var account = new Account(guestId, 1) {
            Status = AccountStatus.Inactive,
            Avatar = new Avatar("guest-avatar"),
        };

        var s = account.PassThroughAllSerializers(Out);

        AssertAccountEqual(s, account);
        s.IsGuest.Should().BeTrue();
    }

    [Fact]
    public void AccountFullSerializationTest_Basic()
    {
        var userId = UserId.New();
        var avatar = new Avatar("avatar-1", 1) {
            Name = "Test Avatar",
            Bio = "Test Bio",
            AvatarKey = "key-1",
        };
        var accountFull = new AccountFull(userId, 42) {
            Status = AccountStatus.Active,
            Avatar = avatar,
            IsAdmin = true,
            SyncContacts = true,
            Phone = ActualChat.Phone.Parse("1-2345678901"),
            Email = "test@example.com",
            Name = "Test User",
            Username = "testuser",
            IsGreetingCompleted = true,
            IsEmailVerified = true,
            CreatedAt = new Moment(DateTime.UtcNow),
            TimeZone = "America/New_York",
        };

        var s = accountFull.PassThroughAllSerializers(Out);

        AssertAccountFullEqual(s, accountFull);
    }

    [Fact]
    public void AccountFullSerializationTest_WithIdentities()
    {
        var userId = UserId.New();
        var identities = ApiMap<UserIdentity, string>.Empty
            .With(new UserIdentity("google", "123456"), "secret1")
            .With(new UserIdentity("email", "test@example.com"), "secret2");
        var claims = ApiMap<string, string>.Empty
            .With("email", "test@example.com")
            .With("name", "Test User");

        var accountFull = new AccountFull(userId, 1) {
            Status = AccountStatus.Active,
            Avatar = new Avatar("avatar-1"),
            JsonCompatibleIdentities = identities.UnorderedItems.ToApiMap(p => p.Key.Id, p => p.Value, StringComparer.Ordinal),
            Claims = claims,
            IsAdmin = false,
            SyncContacts = true,
            Phone = null,
            Email = "test@example.com",
            Name = "Test User",
            Username = "testuser",
            IsGreetingCompleted = false,
            IsEmailVerified = true,
            CreatedAt = new Moment(DateTime.UtcNow),
            TimeZone = "UTC",
            AliasId = null,
        };

        var s = accountFull.PassThroughAllSerializers(Out);

        AssertAccountFullEqual(s, accountFull);
        s.Identities.Count.Should().Be(accountFull.Identities.Count);
        s.Claims.Count.Should().Be(accountFull.Claims.Count);
    }

    [Fact]
    public void AccountFullSerializationTest_WithAlias()
    {
        var userId = UserId.New();
        var aliasId = AliasId.Parse("test-alias");
        var accountFull = new AccountFull(userId, 1) {
            Name = "Alias User",
            AliasId = aliasId,
        };

        var s = accountFull.PassThroughAllSerializers(Out);

        AssertAccountFullEqual(s, accountFull);
        s.AliasId.Should().Be(aliasId);
    }

    [Fact]
    public void AccountFullSerializationTest_Empty()
    {
        var userId = UserId.New();
        var accountFull = new AccountFull(userId);

        var s = accountFull.PassThroughAllSerializers(Out);

        AssertAccountFullEqual(s, accountFull);
    }

    private static void AssertAccountEqual(Account actual, Account expected)
    {
        actual.Id.Should().Be(expected.Id);
        actual.Version.Should().Be(expected.Version);
        actual.Status.Should().Be(expected.Status);
        actual.IsGuest.Should().Be(expected.IsGuest);
        AssertAvatarEqual(actual.Avatar, expected.Avatar);
    }

    private static void AssertAccountFullEqual(AccountFull actual, AccountFull expected)
    {
        AssertAccountEqual(actual, expected);
        actual.IsAdmin.Should().Be(expected.IsAdmin);
        actual.SyncContacts.Should().Be(expected.SyncContacts);
        actual.Phone.Should().Be(expected.Phone);
        actual.Email.Should().Be(expected.Email);
        actual.Name.Should().Be(expected.Name);
        actual.Username.Should().Be(expected.Username);
        actual.IsGreetingCompleted.Should().Be(expected.IsGreetingCompleted);
        actual.IsEmailVerified.Should().Be(expected.IsEmailVerified);
        actual.CreatedAt.Should().Be(expected.CreatedAt);
        actual.TimeZone.Should().Be(expected.TimeZone);
        actual.AliasId.Should().Be(expected.AliasId);
        actual.Identities.Should().BeEquivalentTo(expected.Identities);
        actual.Claims.Should().BeEquivalentTo(expected.Claims);
    }

    private static void AssertAvatarEqual(Avatar? actual, Avatar? expected)
    {
        if (expected == null) {
            actual.Should().BeNull();
            return;
        }
        actual.Should().NotBeNull();
        actual!.Id.Should().Be(expected.Id);
        actual.Version.Should().Be(expected.Version);
        actual.Name.Should().Be(expected.Name);
        actual.Bio.Should().Be(expected.Bio);
        actual.AvatarKey.Should().Be(expected.AvatarKey);
        actual.PictureUrl.Should().Be(expected.PictureUrl);
        actual.MediaId.Should().Be(expected.MediaId);
    }
}
