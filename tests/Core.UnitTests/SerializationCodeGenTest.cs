using ActualChat.Hashing;
using ActualChat.Hosting;
using ActualChat.Search;
using ActualChat.Security;

namespace ActualChat.Core.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        // Core generic types
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<Maybe<int>>();
        SerializationCodeGen.ValidateType<SetDiff<string>>();
        SerializationCodeGen.ValidateType<Expiring<string>>();

        // Mathematics
        SerializationCodeGen.ValidateType<LinearMap>();
        SerializationCodeGen.ValidateType<LinearMapDiff>();
        SerializationCodeGen.ValidateType<Range<int>>();
        SerializationCodeGen.ValidateType<Tile<int>>();

        // Hashing & Security
        SerializationCodeGen.ValidateType<HashString>();
        SerializationCodeGen.ValidateType<SecureToken>();
        SerializationCodeGen.ValidateType<SecureValue>();

        // Search
        SerializationCodeGen.ValidateType<SearchMatch>();
        SerializationCodeGen.ValidateType<SearchMatchPart>();
        SerializationCodeGen.ValidateType<SearchPhrase>();

        // Users
        SerializationCodeGen.ValidateType<SessionInfoFull>();
        SerializationCodeGen.ValidateType<UserIdentity>();

        // Hosting
        SerializationCodeGen.ValidateType<HostRole>();

        // Identifiers
        SerializationCodeGen.ValidateType<NodeRef>();
    }
}
