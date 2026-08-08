using System.ComponentModel.DataAnnotations;
using ActualChat.Localization;
using ActualChat.UI.Blazor.Components;
using ActualChat.Validation;

namespace ActualChat.UI.Blazor.UnitTests;

public class LocalizingValidatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    // English comes from the catalog too, so the wording is the app's own, not the framework's.
    [Fact]
    public void UsesTheCatalogForEnglish()
    {
        var model = new Model { Name = "ab", Email = "not-an-email" };

        Validate(model, null).Should().BeEquivalentTo([
            "Name must be between 3 and 20 characters long.",
            "Email is not a valid e-mail address.",
        ]);
    }

    [Fact]
    public void LocalizesMessagesAndFieldNames()
    {
        var model = new Model { Name = "ab", Email = "not-an-email" };

        Validate(model, "ru").Should().BeEquivalentTo([
            "Поле «Имя» должно содержать от 3 до 20 символов.",
            "«Электронная почта» — недопустимый адрес электронной почты.",
        ]);
    }

    [Fact]
    public void FailedRequiredSuppressesOtherAttributesOnTheSameProperty()
    {
        var model = new Model { Name = "", Email = "a@b.com" };

        Validate(model, "ru").Should().BeEquivalentTo(["Поле «Имя» обязательно для заполнения."]);
    }

    [Fact]
    public void RunsTheSyncHalfOfAsyncAttributes()
    {
        var model = new Model { Name = "abc", Email = "a@b.com", PhoneOrEmail = "neither" };

        Validate(model, "ru").Should().BeEquivalentTo(["Введите номер телефона или адрес электронной почты."]);
    }

    [Fact]
    public void FallsBackToTheAttributeMessageWhenUnmapped()
    {
        var model = new Model { Name = "abc", Email = "a@b.com", Nickname = "toolongnickname" };

        Validate(model, "ru").Should().BeEquivalentTo(["Nickname is too long."]);
    }

    // Private methods

    private static List<string?> Validate(Model model, string? isoCode)
    {
        using var _ = UILanguage.Change(isoCode);
        var results = new List<ValidationResult>();
        LocalizingValidator.ValidateObject(new ValidationContext(model), results);
        return results.Select(x => x.ErrorMessage).ToList();
    }

    // Nested types

    public sealed class Model
    {
        [Required]
        [StringLength(20, MinimumLength = 3)]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [PhoneOrEmailAsync]
        public string PhoneOrEmail { get; set; } = "";

        [StringLength(10, ErrorMessage = "Nickname is too long.")]
        public string Nickname { get; set; } = "";
    }
}
