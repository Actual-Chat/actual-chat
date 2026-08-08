using System.ComponentModel.DataAnnotations;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Resources;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

// The guard test of docs/plans/validation-messages-localization.md: it drives the real
// attributes and validators and asserts every message they produce resolves through the
// catalog. Without it, a reworded validator or a .NET upgrade silently degrades the app
// to AI translation instead of failing the build.

public class ValidationMessageLocalizationTest
{
    public static TheoryData<string, string?, string> ValidatorMessages
        // The branch label is what keeps the rows distinct: three of the email branches
        // produce the same sentence, and xUnit would otherwise collapse them into one case.
        => new() {
            { "email: too long", Validators.Email.Validate(new string('a', 315) + "@x.com"), "Validation_EmailInvalid" },
            { "email: unparsable", Validators.Email.Validate("not-an-email"), "Validation_EmailInvalid" },
            { "email: display name", Validators.Email.Validate("Name <name@x.com>"), "Validation_EmailInvalid" },
            { "phone: bad chars", Validators.Phone.Validate("+1abc2345678"), "Validation_PhoneInvalidCharacters" },
            { "phone: too short", Validators.Phone.Validate("+1234567"), "Validation_PhoneTooShort" },
            { "phone: too long", Validators.Phone.Validate("+1234567890123456"), "Validation_PhoneTooLong" },
            { "phone or email", Error(new PhoneOrEmailAttribute(), "hello"), "Validation_PhoneOrEmailRequired" },
            { "alias: too short", Error(new AliasIdAttribute(), "abc"), "Validation_AliasTooShort" },
            { "alias: bad chars", Error(new AliasIdAttribute(), "abcde!"), "Validation_AliasInvalidCharacters" },
        };

    [Fact]
    public void RequiredMessageShouldMatchItsTemplate()
        => AssertTemplate(new RequiredAttribute(), "Validation_Required_Format");

    [Fact]
    public void MinLengthMessageShouldMatchItsTemplate()
        => AssertTemplate(new MinLengthAttribute(3), "Validation_MinLength_Format");

    [Fact]
    public void EmailAddressMessageShouldMatchItsTemplate()
        => AssertTemplate(new EmailAddressAttribute(), "Validation_EmailAddress_Format");

    [Theory, MemberData(nameof(ValidatorMessages))]
    public void ValidatorMessageShouldResolveExactly(string branch, string? message, string key)
    {
        // arrange
        message.Should().NotBeNull($"'{branch}' must actually fail validation");

        // act
        var match = MessageIndex.Default.Match(message!);

        // assert
        match.Should().NotBeNull($"'{branch}' produces \"{message}\", which must be in Messages.en.json");
        match!.Key.Should().Be(key);
        match.Args.Should().BeEmpty();
    }

    [Fact]
    public void DeleteConfirmationMessagesShouldResolveExactly()
    {
        // The two inline ErrorMessage literals - both carry the literal DELETE token,
        // so they must never reach the AI fallback.

        // act
        var required = Validate(new DeleteAccountModal.FormModel(null), "");
        var mismatched = Validate(new DeleteAccountModal.FormModel(null), "delete");

        // assert
        Key(required).Should().Be("Validation_DeleteConfirmationRequired");
        Key(mismatched).Should().Be("Validation_DeleteConfirmationInvalid");
    }

    [Fact]
    public void LabelShouldReplaceDisplayName()
    {
        // arrange
        var l = Localizer();

        // act
        var message = l.TryMessage("The Short name field is required.", "User link");

        // assert
        message.Should().Be("<User link>!");
    }

    [Fact]
    public void MissingLabelShouldFallBackToFieldCatalog()
    {
        // arrange
        var l = Localizer();

        // act
        var message = l.TryMessage("The Phone or email field is required.");

        // assert
        message.Should().Be("<[phone or email]>!");
    }

    [Fact]
    public void UncataloguedFieldShouldKeepItsEnglishName()
    {
        // arrange
        var l = Localizer();

        // act
        var message = l.TryMessage("The Short name field is required.");

        // assert
        message.Should().Be("<Short name>!");
    }

    [Fact]
    public void NonFieldArgsShouldPassThroughUntouched()
    {
        // A translated '1' - or, worse, a translated regular expression - would be a bug.

        // arrange
        var l = Localizer();

        // act
        var message = l.TryMessage(
            new MinLengthAttribute(1).FormatErrorMessage("Short name"), "User link");

        // assert
        message.Should().Be("<User link>/1");
    }

    [Fact]
    public void RegularExpressionMessageShouldNotResolve()
    {
        // It is deliberately not catalogued: the only usage overrides ErrorMessage, so its
        // framework text never renders. This pins that decision instead of leaving it implicit.

        // act
        var match = MessageIndex.Default.Match(
            new RegularExpressionAttribute("^DELETE$").FormatErrorMessage("Delete"));

        // assert
        match.Should().BeNull();
    }

    [Fact]
    public void UnknownMessageShouldNotResolve()
    {
        // act
        var message = Localizer().TryMessage("Something nobody has catalogued.");

        // assert
        message.Should().BeNull();
    }

    [Fact]
    public void UntranslatedKeyShouldNotResolve()
    {
        // A catalog miss must read as "no tier 1/2 answer", not render the key name.

        // act
        var message = new TestStringLocalizer([]).TryMessage("Email address is invalid.");

        // assert
        message.Should().BeNull();
    }

    // Private methods

    private static void AssertTemplate(ValidationAttribute attribute, string key)
    {
        // arrange
        const string fieldName = "<field>";

        // act
        var message = attribute.FormatErrorMessage(fieldName);
        var match = MessageIndex.Default.Match(message);

        // assert
        match.Should().NotBeNull($"\"{message}\" must match a template in Messages.en.json");
        match!.Key.Should().Be(key);
        match.HasFieldArg.Should().BeTrue();
        match.Args[0].Should().Be(fieldName, "arg 0 of a Validation_ template is the field name");
    }

    private static TestStringLocalizer Localizer()
        => new(new() {
            ["Validation_Required_Format"] = "<{0}>!",
            ["Validation_MinLength_Format"] = "<{0}>/{1}",
            ["Field_PhoneOrEmail"] = "[phone or email]",
        });

    private static string? Error(ValidationAttribute attribute, object? value)
        => attribute.GetValidationResult(value, new ValidationContext(new object()))?.ErrorMessage;

    private static List<string> Validate(DeleteAccountModal.FormModel model, string value)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model) { MemberName = nameof(model.Delete) };
        Validator.TryValidateProperty(value, context, results);
        return results.Select(r => r.ErrorMessage!).ToList();
    }

    private static string? Key(List<string> messages)
    {
        messages.Should().ContainSingle();
        return MessageIndex.Default.Match(messages[0])?.Key;
    }
}
