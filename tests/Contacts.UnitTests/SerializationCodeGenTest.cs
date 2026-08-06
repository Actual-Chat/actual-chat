namespace ActualChat.Contacts.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<ChangedContactsQuery>();
        SerializationCodeGen.ValidateType<Contact>();
        SerializationCodeGen.ValidateType<ExternalContactsHash>();
        SerializationCodeGen.ValidateType<ContactSubset>();
        SerializationCodeGen.ValidateType<ThreadContact>();
    }
}
