using System.ComponentModel.DataAnnotations;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

// The guard test of docs/plans/validation-localization-forward-keys.md. Validation messages take
// two routes and both are pinned here: BCL attributes produce English that MessageIndex reverse-
// matches back to a key, while our own attributes report the key directly.

public class ValidationMessageLocalizationTest
{
    private const string LongEmail = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static TheoryData<string, string?, string> AppAttributeKeys
        // The branch label keeps the rows distinct: three email branches share one key,
        // and xUnit would otherwise collapse them into a single case.
        => new() {
            { "email: too long", Validators.Email.Validate(TooLongEmail()), ValidationKeys.EmailInvalid },
            { "email: unparsable", Validators.Email.Validate("not-an-email"), ValidationKeys.EmailInvalid },
            { "email: display name", Validators.Email.Validate("N <n@x.com>"), ValidationKeys.EmailInvalid },
            { "phone: bad chars", Validators.Phone.Validate("+1abc2345678"), ValidationKeys.PhoneInvalidCharacters },
            { "phone: too short", Validators.Phone.Validate("+1234567"), ValidationKeys.PhoneTooShort },
            { "phone: too long", Validators.Phone.Validate("+1234567890123456"), ValidationKeys.PhoneTooLong },
            { "phone or email", Error(new PhoneOrEmailAttribute(), "hello"), ValidationKeys.PhoneOrEmailRequired },
            { "alias: too short", Error(new AliasIdAttribute(), "abc"), ValidationKeys.AliasTooShort },
            { "alias: bad chars", Error(new AliasIdAttribute(), "abcde!"), ValidationKeys.AliasInvalidCharacters },
        };

    [Theory, MemberData(nameof(AppAttributeKeys))]
    public void AppAttributeShouldReportItsKey(string branch, string? message, string key)
    {
        // assert
        message.Should().Be(key, $"'{branch}' must report a catalog key, not an English sentence");
    }

    [Fact]
    public void AppAttributeKeysShouldResolveInEveryLanguage()
    {
        // These never reach MessageIndex, so nothing else would catch a missing translation.

        // assert
        foreach (var language in LanguageUI.SupportedUILanguages) {
            var catalog = StringCatalog.Load(StringCatalog.StringsPrefix, language.IsoCode);
            catalog.Should().NotBeNull($"'{language.IsoCode}' must ship a catalog");
            foreach (var key in ValidationKeys.All)
                catalog!.Should().ContainKey(key, $"'{key}' must be translated into '{language.IsoCode}'");
        }
    }

    [Fact]
    public void AppAttributeKeyShouldResolveThroughTryKey()
    {
        // arrange
        var l = new TestStringLocalizer(new() { [ValidationKeys.EmailInvalid] = "<email>" });

        // act
        var message = l.TryKey(ValidationKeys.EmailInvalid);

        // assert
        message.Should().Be("<email>");
    }

    [Fact]
    public void NonKeyTextShouldNotResolveThroughTryKey()
    {
        // An English sentence must fall through to the reverse index, not be read as a key.

        // act
        var message = Localizer().TryKey("The Short name field is required.");

        // assert
        message.Should().BeNull();
    }

    [Fact]
    public void RequiredMessageShouldMatchItsTemplate()
        => AssertTemplate(new RequiredAttribute(), "Validation_Required_Format");

    [Fact]
    public void MinLengthMessageShouldMatchItsTemplate()
        => AssertTemplate(new MinLengthAttribute(3), "Validation_MinLength_Format");

    [Fact]
    public void EmailAddressMessageShouldMatchItsTemplate()
        => AssertTemplate(new EmailAddressAttribute(), "Validation_EmailAddress_Format");

    [Fact]
    public void DeleteConfirmationMessagesShouldResolveExactly()
    {
        // Both literals carry the DELETE token, so they must never reach the AI fallback.

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
        // Only {field} is substituted; a translated '1' or regular expression would be a bug.

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
        // Deliberately not catalogued: its only usage overrides ErrorMessage.

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
        // A catalog miss must read as "no answer here", not render the key name.

        // act
        var message = new TestStringLocalizer([]).TryMessage("The Short name field is required.");

        // assert
        message.Should().BeNull();
    }

    // Private methods

    private static string TooLongEmail()
        => string.Concat(Enumerable.Repeat(LongEmail, 7)) + "@x.com";

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
        match.Args.Should().ContainKey(MessageIndex.FieldArg,
            "a Validation_ template names its field placeholder {field}");
        match.Args[MessageIndex.FieldArg].Should().Be(fieldName);
    }

    private static TestStringLocalizer Localizer()
        => new(new() {
            ["Validation_Required_Format"] = "<{field}>!",
            ["Validation_MinLength_Format"] = "<{field}>/{min}",
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
