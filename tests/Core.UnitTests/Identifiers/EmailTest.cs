namespace ActualChat.Core.UnitTests.Identifiers;

public class EmailTest(ITestOutputHelper @out) : StringIdentifierTestBase<Email>(@out)
{
    public override string[] ValidIdentifiers { get; } = [
        "test@example.com",
        "user.name@example.com",
        "user_name@example.co.uk",
        "test-email@sub.domain.com",
        "a@b.co",
    ];

    public override string[] InvalidIdentifiers { get; } = [
        "",
        "invalid",
        "@example.com",
        "user@",
        "user@@example.com",
        "user name@example.com",
        "user@exam ple.com",
    ];

    [Fact]
    public void NormalizationTest()
    {
        var email1 = Email.Parse("Test@Example.Com");
        var email2 = Email.Parse("test@example.com");

        // Both should be normalized to lowercase
        email1.Value.Should().Be("test@example.com");
        email2.Value.Should().Be("test@example.com");

        // Both should be equal
        email1.Should().Be(email2);

        // Hash should be the same for normalized emails
        email1.Hash.Should().Be(email2.Hash);

        // IsNormalized should return true for lowercase
        email1.IsNormalized().Should().BeTrue();
        email2.IsNormalized().Should().BeTrue();
    }

    [Fact]
    public void CacheTest()
    {
        // Parse same email with different cases
        var email1 = Email.Parse("Test@Example.Com");
        var email2 = Email.Parse("TEST@EXAMPLE.COM");
        var email3 = Email.Parse("test@example.com");

        // All should reference the same cached instance
        ReferenceEquals(email1, email2).Should().BeTrue();
        ReferenceEquals(email2, email3).Should().BeTrue();
        ReferenceEquals(email1, email3).Should().BeTrue();
    }
}
