using ActualChat.UI.Blazor.Resources;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class MessageIndexTest
{
    [Fact]
    public void ShippedCatalogShouldBuild()
    {
        // The constructor is where the catalog's invariants live: known routing prefixes,
        // unique English values per index, no adjacent placeholders. Touching Default runs them.

        // act
        var build = () => MessageIndex.Default;

        // assert
        build.Should().NotThrow();
    }

    [Fact]
    public void TemplateShouldExtractItsArgument()
    {
        // arrange
        var index = New(("Validation_Required_Format", "The {field} field is required."));

        // act
        var match = index.Match("The Email field is required.");

        // assert
        match.Should().NotBeNull();
        match!.Key.Should().Be("Validation_Required_Format");
        match.Args.Should().Equal(new Dictionary<string, string> { ["field"] = "Email" });
    }

    [Fact]
    public void TemplateShouldExtractSeparatedArguments()
    {
        // arrange
        var index = New(("Validation_MinLength_Format", "The field {field} must be at least '{min}' long."));

        // act
        var match = index.Match("The field Short name must be at least '5' long.");

        // assert
        match!.Args.Should().Equal(new Dictionary<string, string> {
            ["field"] = "Short name",
            ["min"] = "5",
        });
    }

    [Fact]
    public void TemplateShouldNotMatchOtherText()
    {
        // arrange
        var index = New(("Validation_Required_Format", "The {field} field is required."));

        // act
        var match = index.Match("The Email field is optional.");

        // assert
        match.Should().BeNull();
    }

    [Fact]
    public void ExactEntryShouldBeatTemplate()
    {
        // This is how a per-field sentence overrides the generic template in languages
        // where interpolating a noun into a fixed frame reads poorly.

        // arrange
        var index = New(
            ("Validation_Required_Format", "The {field} field is required."),
            ("Validation_PhoneRequired", "The Phone field is required."));

        // act
        var match = index.Match("The Phone field is required.");

        // assert
        match!.Key.Should().Be("Validation_PhoneRequired");
        match.Args.Should().BeEmpty();
    }

    [Fact]
    public void MostSpecificTemplateShouldWin()
    {
        // arrange
        var index = New(
            ("Validation_Invalid_Format", "{field} is invalid."),
            ("Validation_FieldInvalid_Format", "The {field} field is invalid."));

        // act
        var match = index.Match("The Email field is invalid.");

        // assert
        match!.Key.Should().Be("Validation_FieldInvalid_Format");
        match.Args.Should().Equal(new Dictionary<string, string> { ["field"] = "Email" });
    }

    [Fact]
    public void ErrorTemplateShouldNotCarryFieldArg()
    {
        // A server message names its placeholders too, but none of them is {field} -
        // substituting a form label into a value would be a bug.

        // arrange
        var index = New(("Error_MessageLimit_Format", "You can send up to {count} messages."));

        // act
        var match = index.Match("You can send up to 5 messages.");

        // assert
        match!.Args.Should().NotContainKey(MessageIndex.FieldArg);
        match.Args.Should().Equal(new Dictionary<string, string> { ["count"] = "5" });
    }

    [Fact]
    public void AdjacentPlaceholdersShouldBeRejected()
    {
        // act
        var build = () => New(("Validation_Bad_Format", "Value {a}{b} is invalid."));

        // assert
        build.Should().Throw<Exception>().WithMessage("*adjacent*");
    }

    [Fact]
    public void SharedEnglishValueShouldBeRejected()
    {
        // act
        var build = () => New(("Validation_A", "Same text."), ("Validation_B", "Same text."));

        // assert
        build.Should().Throw<Exception>().WithMessage("*share*");
    }

    [Fact]
    public void SharedTemplateShouldBeRejected()
    {
        // act
        var build = () => New(
            ("Validation_A_Format", "The {field} field is required."),
            ("Validation_B_Format", "The {field} field is required."));

        // assert
        build.Should().Throw<Exception>().WithMessage("*share*");
    }

    [Fact]
    public void UnknownKeyPrefixShouldBeRejected()
    {
        // A typo'd prefix would otherwise produce an entry that is loaded for forward
        // lookup but never indexed - i.e. silently falls through to AI translation.

        // act
        var build = () => New(("Label_Whatever", "Some text."));

        // assert
        build.Should().Throw<Exception>().WithMessage("*must start with*");
    }

    // Private methods

    private static MessageIndex New(params (string Key, string Message)[] entries)
        => new(entries.ToDictionary(x => x.Key, x => x.Message));
}
