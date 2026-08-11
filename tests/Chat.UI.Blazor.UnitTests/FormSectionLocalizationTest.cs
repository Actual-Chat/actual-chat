using System.ComponentModel.DataAnnotations;
using ActualChat.Hosting;
using Bunit;
using Microsoft.Extensions.Hosting;
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
        // LocalizedMessage is a ComputedStateComponent<UIHub, string>, so rendering a FormSection
        // needs a resolvable UIHub - the smallest set its constructor reads eagerly.
        var hostInfo = new HostInfo {
            HostKind = HostKind.Server,
            AppKind = AppKind.Unknown,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        var context = new BunitContext();
        context.Services
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddScoped<UIHub>()
            .AddFusion(fusion => fusion.AddBlazor());
        // LocalizedMessage is a ComputedStateComponent, which reaches its hub as
        // (UIHub)CircuitHub - BlazorUICoreModule aliases the two for the same reason. This has to
        // come after AddBlazor, whose own CircuitHub registration would otherwise win.
        context.Services.AddScoped<CircuitHub>(c => c.GetRequiredService<UIHub>());
        context.Services.AddSingleton<IStringLocalizer>(new TestStringLocalizer(new() {
            ["Validation_Required_Format"] = "<{field}>!",
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
