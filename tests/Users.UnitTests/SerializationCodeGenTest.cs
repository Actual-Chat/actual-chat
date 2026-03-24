namespace ActualChat.Users.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<ServerKvasBackend_SetMany>();
        SerializationCodeGen.ValidateType<Account>();
        SerializationCodeGen.ValidateType<AccountFull>();
        SerializationCodeGen.ValidateType<Avatar>();
        SerializationCodeGen.ValidateType<AvatarFull>();
        SerializationCodeGen.ValidateType<ChatPosition>();
        SerializationCodeGen.ValidateType<UserLanguageSettings>();
        SerializationCodeGen.ValidateType<UserAppSettings>();
        SerializationCodeGen.ValidateType<UserOnboardingSettings>();
    }
}
