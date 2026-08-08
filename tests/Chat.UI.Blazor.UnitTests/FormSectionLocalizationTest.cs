using System.ComponentModel.DataAnnotations;
using Bunit;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class FormSectionLocalizationTest
{
    [Fact]
    public void ValidationMessageShouldRenderLocalizedWithTheLabel()
    {
        // OwnAccountEditorModal's alias field is exactly this case: the label reads
        // "User link" while [Display] says "Short name", so the two disagree by design.

        // arrange
        using var context = NewContext();
        var form = new TestForm();
        var editContext = Invalidate(form);

        // act
        var cut = context.Render<FormSection<string>>(p => p
            .Add(x => x.For, () => form.Name)
            .Add(x => x.Label, "User link")
            .AddCascadingValue(editContext));

        // assert
        cut.Find(".form-section-validation").TextContent.Should().Be("<User link>!");
    }

    [Fact]
    public void ValidSectionShouldRenderAnEmptyValidationSlot()
    {
        // `.form-section-validation:not(:empty)` paints a background, and :empty counts a
        // whitespace text node as content - so the slot must stay literally empty.

        // arrange
        using var context = NewContext();
        var form = new TestForm { Name = "ok" };

        // act
        var cut = context.Render<FormSection<string>>(p => p
            .Add(x => x.For, () => form.Name)
            .Add(x => x.Label, "User link")
            .AddCascadingValue(new EditContext(form)));

        // assert
        cut.Find(".form-section-validation").InnerHtml.Should().BeEmpty();
    }

    [Fact]
    public void ValidationMessageFragmentShouldReceiveTheLabel()
    {
        // The RenderFragment<string> is what ties AliasValidationMessage's FieldLabel to
        // the enclosing section's Label instead of to a copy of the literal.

        // arrange
        using var context = NewContext();
        var form = new TestForm();
        var editContext = Invalidate(form);

        // act
        var cut = context.Render<FormSection<string>>(p => p
            .Add(x => x.For, () => form.Name)
            .Add(x => x.Label, "User link")
            .Add(x => x.ValidationMessage, label => builder => builder.AddContent(0, $"[{label}]"))
            .AddCascadingValue(editContext));

        // assert
        cut.Markup.Should().Contain("[User link]");
    }

    // Private methods

    private static BunitContext NewContext()
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IStringLocalizer>(new TestStringLocalizer(new() {
            ["Validation_Required_Format"] = "<{0}>!",
        }));
        return context;
    }

    private static EditContext Invalidate(TestForm form)
    {
        var editContext = new EditContext(form);
        var store = new ValidationMessageStore(editContext);
        var results = new List<ValidationResult>();
        var validationContext = new ValidationContext(form) { MemberName = nameof(TestForm.Name) };
        Validator.TryValidateProperty(form.Name, validationContext, results);
        store.Add(editContext.Field(nameof(TestForm.Name)), results.Select(r => r.ErrorMessage!));
        editContext.NotifyValidationStateChanged();
        return editContext;
    }

    // Nested types

    private sealed class TestForm
    {
        [Required]
        [Display(Name = "Short name")]
        public string Name { get; set; } = "";
    }
}
