namespace ActualChat.Users.UnitTests;

public class AccountNameValidatorTest
{
    private readonly AccountNameValidator _validator = new();

    [Theory]
    [InlineData("John", null)] // Valid: minimum length
    [InlineData("John Doe", null)] // Valid: with space
    [InlineData("O'Brien", null)] // Valid: with apostrophe
    [InlineData("Test: Name", null)] // Valid: with colon
    [InlineData("Name-With-Dashes", null)] // Valid: with dashes
    [InlineData("", "empty")]
    [InlineData("abc", "short")]
    [InlineData(" John", "starts")]
    [InlineData("John ", "ends")]
    [InlineData("John  Doe", "consecutive")]
    [InlineData("John\tDoe", "control")]
    [InlineData("John\nDoe", "control")]
    [InlineData("John\u200BDoe", "invisible")]
    [InlineData("John\u202EDoe", "control")]
    public void Validate_ShouldReturnExpectedResult(string name, string? expectedErrorPart)
    {
        var error = _validator.Validate(name);
        if (expectedErrorPart == null)
            error.Should().BeNull();
        else
            error.Should().ContainEquivalentOf(expectedErrorPart);
    }

    [Theory]
    [InlineData("John Doe", "John Doe")] // Already valid
    [InlineData("John  Doe", "John Doe")] // Consecutive spaces normalized
    [InlineData("  John  ", "John")] // Trimmed and normalized
    [InlineData("John\u200BDoe", "JohnDoe")] // Zero-width space removed
    [InlineData("John\t\nDoe", "JohnDoe")] // Control chars removed
    public void Normalize_ShouldCleanupName(string name, string expected)
        => _validator.Normalize(name).Should().Be(expected);

    [Theory]
    [InlineData("")] // Empty becomes generated
    [InlineData("ab")] // Too short gets prefix
    [InlineData("   ")] // Only spaces becomes generated
    public void Normalize_ShouldPrependGeneratedName_WhenTooShort(string name)
    {
        var result = _validator.Normalize(name);
        result.Should().MatchRegex(@"^[A-Z][a-z]+\s[A-Z][a-z]+"); // Matches "Word Word" pattern
        result.Length.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Normalize_ShouldBeIdempotent()
    {
        var name = "Test  Name\u200B";
        var first = _validator.Normalize(name);
        var second = _validator.Normalize(first);
        second.Should().Be(first);
    }
}
