namespace ActualChat.Core.UnitTests.Identifiers;

public class PlaceIdIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<PlaceId>(@out)
{
    public override string[] ValidIdentifiers => new[] { "12345abcde", "whateveritis" }
        .Concat(Constants.Place.SystemPlaceIdValues)
        .ToArray();
    public override string[] InvalidIdentifiers => [ "x", "1234abcd", "some:chat", "peer-chat", "~guest~1" ];
}
