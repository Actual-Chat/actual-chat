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
        foreach (var language in Languages.AllUI) {
            var catalog = StringCatalogs.LoadStrings(language);
            catalog.Should().NotBeNull($"'{language.IsoCode}' must ship a catalog");
            foreach (var key in AllValidationKeys())
                catalog!.Should().ContainKey(key, $"'{key}' must be translated into '{language.IsoCode}'");
        }
    }

    [Fact]
    public void AppAttributeKeyShouldResolveDirectly()
    {
        // arrange
        var l = new TestStringLocalizer(new() { [ValidationKeys.EmailInvalid] = "<email>" });

        // act
        var message = l.ForValidationKey(ValidationKeys.EmailInvalid);

        // assert
        message.Should().Be("<email>");
    }

    [Fact]
    public void EnglishTextShouldNotResolveAsAKey()
    {
        // An English sentence must fall through to the reverse index, not be read as a key.

        // act
        var message = Localizer().ForValidationKey("The Short name field is required.");

        // assert
        message.Should().BeNull();
    }

    [Fact]
    public void NoValidationMessageShouldNeedTheAiFallback()
    {
        // AI translation is a safety net for text nobody catalogued. No validation message may
        // rely on it: each one must resolve from the shipped catalog, in every language, through
        // whichever of the two routes produced it.

        // assert
        foreach (var language in Languages.AllUI) {
            var l = new TestStringLocalizer(ShippedCatalog(language));
            var iso = language.IsoCode;
            foreach (var key in AllValidationKeys())
                l.ForValidationKey(key).Should().NotBeNull($"'{key}' must resolve in '{iso}'");
            foreach (var attribute in BclAttributes) {
                var message = attribute.FormatErrorMessage("Field");
                l.ForRuntimeMessage(message).Should().NotBeNull($"\"{message}\" must resolve in '{iso}'");
            }
            foreach (var message in DeleteConfirmationMessages())
                l.ForRuntimeMessage(message).Should().NotBeNull($"\"{message}\" must resolve in '{iso}'");
        }
    }

    [Fact]
    public void EveryBclAttributeOnAFormModelShouldBeCatalogued()
    {
        // The catalog only knows the BCL attributes we already use. Adding a [Range] or
        // [StringLength] to a form model would render .NET's own English and fall through to AI
        // translation - silently, and only in non-English. This turns that into a build failure,
        // and the fix is to add the template to Messages.en.json plus its translations.
        // Our own attributes are skipped: they report a key, which AllValidationKeys covers.

        // act
        var models = FormModelTypes().ToList();
        var scanned = 0;
        var offenders = new List<string>();
        foreach (var model in models)
        foreach (var property in model.GetProperties())
        foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>(inherit: true)) {
            if (attribute.GetType().Namespace?.StartsWith("System.", StringComparison.Ordinal) != true)
                continue;

            scanned++;
            var message = Format(attribute);
            if (message == null || MessageIndex.Default.Match(message) == null)
                offenders.Add($"{model.FullName}.{property.Name}: [{attribute.GetType().Name}] -> {message ?? "<threw>"}");
        }

        // assert
        // A guard that discovers nothing would pass silently, so pin that it still finds models
        // and attributes - reflection breaks quietly when a base type or assembly moves.
        models.Should().HaveCountGreaterThan(10, "form models must still be discoverable");
        scanned.Should().BeGreaterThan(10, "BCL validation attributes must still be discoverable");
        offenders.Should().BeEmpty(
            "every BCL validation attribute in use must be in Messages.en.json:\n{0}",
            string.Join("\n", offenders));
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
        var message = l.ForRuntimeMessage("The Short name field is required.", "User link");

        // assert
        message.Should().Be("<User link>!");
    }

    [Fact]
    public void MissingLabelShouldKeepTheFrameworkFieldName()
    {
        // A section with no Label has nothing better to offer than the member name.

        // arrange
        var l = Localizer();

        // act
        var message = l.ForRuntimeMessage("The Short name field is required.");

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
        var message = l.ForRuntimeMessage(
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
        var message = Localizer().ForRuntimeMessage("Something nobody has catalogued.");

        // assert
        message.Should().BeNull();
    }

    [Fact]
    public void UntranslatedKeyShouldNotResolve()
    {
        // A catalog miss must read as "no answer here", not render the key name.

        // act
        var message = new TestStringLocalizer([]).ForRuntimeMessage("The Short name field is required.");

        // assert
        message.Should().BeNull();
    }

    // Private methods

    // Not every EditForm model derives from FormModel - most are plain nested classes - so the
    // scan is "any type on a UI layer that carries validation attributes" instead.
    private static IEnumerable<Type> FormModelTypes()
        => new[] { typeof(FormModel).Assembly, typeof(DeleteAccountModal).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetProperties().Any(p => p.GetCustomAttributes<ValidationAttribute>(true).Any()));

    private static string? Format(ValidationAttribute attribute)
    {
        try {
            return attribute.FormatErrorMessage("TestField");
        }
        catch (Exception) {
            // An attribute we can't even format is one we certainly can't localize.
            return null;
        }
    }

    private static ValidationAttribute[] BclAttributes
        => [new RequiredAttribute(), new MinLengthAttribute(3), new EmailAddressAttribute()];

    // Read off ValidationKeys itself, so a key added there is covered without a second list.
    private static List<string> AllValidationKeys()
    {
        var keys = typeof(ValidationKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.FieldType == typeof(string) && x.Name != nameof(ValidationKeys.Prefix))
            .Select(x => (string)x.GetValue(null)!)
            .ToList();
        keys.Should().NotBeEmpty("a reflection miss would make every caller pass vacuously");
        return keys;
    }

    private static IEnumerable<string> DeleteConfirmationMessages()
        => Validate(new DeleteAccountModal.FormModel(null), "")
            .Concat(Validate(new DeleteAccountModal.FormModel(null), "delete"));

    // Mirrors AppStringLocalizer.LoadAll: one forward lookup over both catalogs.
    private static Dictionary<string, string> ShippedCatalog(Language language)
    {
        var strings = StringCatalogs.LoadStrings(language)!;
        var messages = StringCatalogs.LoadMessages(language);
        if (messages != null)
            foreach (var (key, value) in messages)
                strings[key] = value;
        return strings;
    }

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
