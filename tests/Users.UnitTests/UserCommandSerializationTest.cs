namespace ActualChat.Users.UnitTests;

public class UserCommandSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    // API Commands

    [Fact]
    public void Accounts_Update_Basic()
    {
        var account = new AccountFull(TestUserId, 1) {
            Name = "Updated",
        };
        var cmd = new Accounts_Update(TestSession, account, 1);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.Account.Id.Should().Be(original.Account.Id);
            }, Out);
    }

    [Fact]
    public void Accounts_DeleteOwn_Basic()
    {
        var cmd = new Accounts_DeleteOwn(TestSession);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Accounts_SignOut_Basic()
    {
        var cmd = new Accounts_SignOut(TestSession);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Accounts_CreateApiKey_Basic()
    {
        var cmd = new Accounts_CreateApiKey(TestSession, "My API Key", 30);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.Name.Should().Be(original.Name);
                deserialized.ExpiresInDays.Should().Be(30);
            }, Out);
    }

    [Fact]
    public void Accounts_CreateApiKey_DefaultExpiration()
    {
        var cmd = new Accounts_CreateApiKey(TestSession, "Default Expiry Key");
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ExpiresInDays.Should().Be(365);
            }, Out);
    }

    [Fact]
    public void Accounts_DeactivateSession_Basic()
    {
        var cmd = new Accounts_DeactivateSession(TestSession, "abc123def456");
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.IdPrefix.Should().Be(original.IdPrefix);
            }, Out);
    }

    [Fact]
    public void Accounts_DeactivateAllSessions_Basic()
    {
        var cmd = new Accounts_DeactivateAllSessions(TestSession, [SessionKind.Session]);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Kinds.Should().BeEquivalentTo(original.Kinds);
            }, Out);
    }

    [Fact]
    public void AccountsBackend_SignOut_Basic()
    {
        var cmd = new AccountsBackend_SignOut(TestSession);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Avatars_Change_Create()
    {
        var cmd = new Avatars_Change(TestSession, Symbol.Empty, null,
            Change.Create(new AvatarDiff { Name = "New Avatar" }));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
            }, Out);
    }

    [Fact]
    public void Avatars_SetDefault_Basic()
    {
        var cmd = new Avatars_SetDefault(TestSession, "avatar-1");
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatPositions_Set_Basic()
    {
        var position = new ChatPosition(42, "origin");
        var cmd = new ChatPositions_Set(TestSession, TestChatId, ChatPositionKind.Read, position);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatPositions_Set_Heard()
    {
        var position = new ChatPosition(42, "origin");
        var cmd = new ChatPositions_Set(TestSession, TestChatId, ChatPositionKind.Heard, position);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatUsages_RegisterUsage_Basic()
    {
        var cmd = new ChatUsages_RegisterUsage(TestSession, ChatUsageListKind.ViewedGroupChats, TestChatId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Emails_SendDigest_Basic()
    {
        var cmd = new Emails_SendDigest(TestSession);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void PhoneAuth_SendTotp_Basic()
    {
        var phone = ActualChat.Phone.Parse("1-2345678901");
        var cmd = new PhoneAuth_SendTotp(TestSession, phone);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void PhoneAuth_ValidateTotp_Basic()
    {
        var phone = ActualChat.Phone.Parse("1-2345678901");
        var cmd = new PhoneAuth_ValidateTotp(TestSession, phone, 123456);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void PhoneAuth_VerifyPhone_Basic()
    {
        var phone = ActualChat.Phone.Parse("1-2345678901");
        var cmd = new PhoneAuth_VerifyPhone(TestSession, phone, 123456);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void EmailAuth_SendTotp_Basic()
    {
        var cmd = new EmailAuth_SendTotp(TestSession, ActualChat.Email.Parse("test@example.com"));
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void EmailAuth_ValidateTotp_Basic()
    {
        var cmd = new EmailAuth_ValidateTotp(TestSession, ActualChat.Email.Parse("test@example.com"), 123456);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void EmailAuth_VerifyEmail_Basic()
    {
        var cmd = new EmailAuth_VerifyEmail(TestSession, ActualChat.Email.Parse("test@example.com"), 123456);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserPresences_CheckIn_Basic()
    {
        var cmd = new UserPresences_CheckIn(TestSession, true);
        cmd.AssertPassesThroughSerializers();
    }

    // Backend Commands

    [Fact]
    public void AccountsBackend_Update_Basic()
    {
        var account = new AccountFull(TestUserId, 1) { Name = "Test" };
        var cmd = new AccountsBackend_Update(account, 1);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Account.Id.Should().Be(original.Account.Id);
            }, Out);
    }

    [Fact]
    public void AccountsBackend_Delete_Basic()
    {
        var cmd = new AccountsBackend_Delete(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void AvatarsBackend_Change_Basic()
    {
        var cmd = new AvatarsBackend_Change("avatar-1", null,
            Change.Create(new AvatarDiff { Name = "Test", UserId = TestUserId }));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.AvatarId.Should().Be(original.AvatarId);
            }, Out);
    }

    [Fact]
    public void ChatPositionsBackend_Set_Basic()
    {
        var position = new ChatPosition(42, "origin");
        var cmd = new ChatPositionsBackend_Set(TestUserId, TestChatId, ChatPositionKind.Read, position);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatPositionsBackend_Set_Heard()
    {
        var position = new ChatPosition(42, "origin");
        var cmd = new ChatPositionsBackend_Set(TestUserId, TestChatId, ChatPositionKind.Heard, position);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatUsagesBackend_RegisterUsage_Basic()
    {
        var cmd = new ChatUsagesBackend_RegisterUsage(TestUserId, ChatUsageListKind.ViewedGroupChats, TestChatId, new Moment(DateTime.UtcNow));
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void EmailsBackend_SendDigest_Basic()
    {
        var cmd = new EmailsBackend_SendDigest(TestUserId) { IsDiagnosticsEnabled = false };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserPresencesBackend_CheckIn_Basic()
    {
        var cmd = new UserPresencesBackend_CheckIn(TestUserId, new Moment(DateTime.UtcNow), true);
        cmd.AssertPassesThroughSerializers();
    }
}
