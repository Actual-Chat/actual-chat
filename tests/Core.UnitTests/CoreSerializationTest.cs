using ActualChat.Hashing;
using ActualChat.Search;
using ActualChat.Security;

namespace ActualChat.Core.UnitTests;

public class CoreSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Change_Create()
    {
        var change = Change.Create("hello");
        change.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Change_Update()
    {
        var change = Change.Update("hello");
        change.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Change_Remove()
    {
        var change = Change.Remove<string>();
        change.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Maybe_WithValue()
    {
        var maybe = Maybe.Value(42);
        maybe.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Maybe_None()
    {
        var maybe = Maybe.None<int>();
        maybe.AssertPassesThroughSerializers();
    }

    [Fact]
    public void HashString_Basic()
    {
        var hash = new HashString("SHA256 Base16 abc123def456");
        hash.AssertPassesThroughSerializers();
    }

    [Fact]
    public void HashString_None()
    {
        var hash = HashString.None;
        hash.AssertPassesThroughSerializers();
    }

    [Fact]
    public void SetDiff_Basic()
    {
        var diff = new SetDiff<AuthorId[], AuthorId> {
            AddedItems = [AuthorId.New(ChatId.Parse("the-actual-one"), 1)],
            RemovedItems = [AuthorId.New(ChatId.Parse("the-actual-one"), 2)],
        };
        var s = diff.PassThroughSerializers(Out);
        s.AddedItems.Length.Should().Be(1);
        s.AddedItems[0].Should().Be(diff.AddedItems[0]);
        s.RemovedItems.Length.Should().Be(1);
        s.RemovedItems[0].Should().Be(diff.RemovedItems[0]);
    }

    [Fact]
    public void SearchMatch_Basic()
    {
        var match = new SearchMatch("test query", 0.95, []);
        var s = match.PassThroughSerializers(Out);
        s.Text.Should().Be(match.Text);
        s.Rank.Should().Be(match.Rank);
        s.Parts.Should().BeEmpty();

        // Query-mode match: parts are computed lazily and serialize as explicit parts.
        var queryMatch = new SearchMatch("McDonalds", new SearchQuery("don"));
        var qs = queryMatch.PassThroughSerializers(Out);
        qs.Text.Should().Be("McDonalds");
        qs.Parts.Should().HaveCount(1);
    }

    [Fact]
    public void SecureToken_Basic()
    {
        var token = new SecureToken("abc123", new Moment(DateTime.UtcNow) + TimeSpan.FromHours(1));
        token.AssertPassesThroughSerializers();
    }

    [Fact]
    public void SecureValue_Basic()
    {
        var value = new DecryptedSecureToken("secret", new Moment(DateTime.UtcNow) + TimeSpan.FromHours(1));
        value.AssertPassesThroughSerializers();
    }

    [Fact]
    public void NodeRef_Basic()
    {
        var nodeRef = NodeRef.Parse("1234abcd");
        nodeRef.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserIdentity_Basic()
    {
        var identity = new UserIdentity(AuthSchema.Email, "test@example.com");
        identity.AssertPassesThroughSerializers();
    }
}
