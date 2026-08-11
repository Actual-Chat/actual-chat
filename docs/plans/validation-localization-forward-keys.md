# Validation Localization — Adopt the .NET Validation Pipeline

**Date:** 2026-08-11
**Branch:** `feat/3721-app-localization` (supersedes the validation half of
`docs/plans/validation-messages-localization.md`)
**Status:** Proposal — every claim below verified by running it on the installed
`11.0.0-preview.6.26359.118` SDK; scratch project at §9

## 0. TL;DR

.NET 11 has a validation-localization pipeline, and **it works today, with our catalog,
under `InvariantGlobalization`, driven by `LanguageUI` rather than `CultureInfo`.** Proven
end-to-end in a scratch Blazor project — `[Required]` on a real `EditContext` renders
`Заполните поле «Псевдоним».` with the display name localized too.

It is keyed **forwards**: `typeof(RequiredAttribute)` → `Validation_Required` → catalog →
`string.Format`. That deletes `MessageIndex`'s entire role in validation — the regex that
reverse-matches .NET's own English sentences back into keys goes away, and `MessageIndex`
shrinks to what it's actually for: the `Error_` server strings.

**Recommendation: adopt it now, in one phase.** The earlier draft of this plan hedged with
a hand-rolled Phase 1 because the API looked abandoned. It isn't — it's consolidating (§1),
and the delta between the shape we have installed and the shape on `main` is about ten
lines of DI configuration.

What it costs, all of it verified:

| Work | Size |
|---|---|
| Move 17 form models from `.razor` `@code` into `.razor.cs` code-behinds | 16 files |
| `[ValidatableType]` on each; `[SkipValidation]` on `FormModel<T>.Base` | 18 lines |
| `AddValidation()` + localization config in `UI.Blazor` and `UI.Blazor.App` | 2 call sites |
| `IStringLocalizerFactory` shim over `AppStringLocalizer` | ~10 lines |
| `[Display(Name = "Field_*")]` → catalog keys | 12 sites |
| App attributes self-localize via `ValidationContext` | 4 attributes |
| Catalog: reverse templates → forward keys | ~13 keys × 14 languages |
| Delete `MessageIndex`'s template/regex machinery + `AsyncDataAnnotationsValidator` | net negative |

The one real risk is preview churn (§8). It is bounded and isolated to a single DI file.

## 1. The API is consolidating, not abandoned

`ValidationLocalizationOptions` is real and works. It is also already being folded into the
core package. Both statements are true, and the second is the good news.

| | `11.0.0-preview.5` / `.6` (installed) | `dotnet/aspnetcore` main, sha `330e89cc` |
|---|---|---|
| Package | separate `Microsoft.Extensions.Validation.Localization` | none — merged into `Microsoft.Extensions.Validation` |
| Wiring | `services.AddValidationLocalization(o => …)` | `services.AddValidation(o => …)` |
| Localizer selection | `ValidationLocalizationOptions.LocalizerProvider` | `ValidationOptions.LocalizerProvider` |
| Key convention | `ValidationLocalizationOptions.ErrorMessageKeyProvider` | `ValidationOptions.MessageKeyProvider` |
| Key context | `ErrorMessageLocalizationContext { Attribute, MemberName, DeclaringType, DisplayName }` | `ValidationMessageKeyContext { ValidatorType, MemberName, DeclaringType }` |
| Extra `{1}+` args | `IValidationAttributeFormatter` + `ValidationAttributeFormatterRegistry` | `IValidationMessageFormatter`, implemented on the attribute |
| Indirection | `IValidationLocalizer` on `ValidationOptions.Localizer` | removed — resolution inlined into the generated code |

Same mechanism, fewer moving parts, one less package. `PublicAPI.Unshipped.txt` also drops
`ValidatableTypeInfo` / `ValidatablePropertyInfo` / `ValidatableParameterInfo` /
`ValidationErrorContext` in favour of interfaces — the concrete classes become
generator-emitted `file` types. None of that is API we would touch.

**Migration cost for us, when preview.7 lands:** rename two options properties, move
`AddValidationLocalization` into `AddValidation`, and read `ctx.ValidatorType` instead of
`ctx.Attribute.GetType()`. We have no `IValidationAttributeFormatter` implementations to
port because every UI-facing message is `{0}`-only (§3.2).

### 1.1 The mechanism

Generated into the app assembly by `ValidationsGenerator`, and mirrored by
`Components.Endpoints.Forms.DataAnnotationsLocalizer` for the SSR client payload:

```csharp
var lookupKey = !string.IsNullOrEmpty(attribute.ErrorMessage)
    ? attribute.ErrorMessage                       // explicit ErrorMessage wins
    : options.MessageKeyProvider?.Invoke(new ValidationMessageKeyContext { … });
if (string.IsNullOrEmpty(lookupKey))
    return result.ErrorMessage;                    // not localized — pass through

var localizer = options.LocalizerProvider(declaringType, localizerFactory);
var localizedTemplate = localizer[lookupKey];
if (localizedTemplate.ResourceNotFound)
    return result.ErrorMessage;                    // miss — pass through

return FormatErrorMessage(attribute, CultureInfo.CurrentCulture, localizedTemplate.Value, displayName);
```

`FormatErrorMessage` is a `switch` supplying each BCL attribute's extra arguments
(`Range` → min/max, `MinLength` → length, `StringLength` → max/min, …), with
`IValidationMessageFormatter` as the escape hatch for custom attributes.

Display names take the same road, with the `[Display(Name = …)]` literal used as **both
lookup key and fallback**:

```csharp
var localizedName = localizer[_literal];
return localizedName.ResourceNotFound ? _literal : localizedName.Value;
```

Two properties matter for us. `CultureInfo` is used **only** as the `string.Format`
provider — never for lookup. And a miss anywhere falls through to the original English
rather than rendering a raw key.

## 2. What has to be true for it to run — all verified

Each of the following was tested in the scratch project (§9), not inferred.

### 2.1 The model type must be *discovered*, or nothing localizes

`EditContextDataAnnotationsExtensions` branches per model type:

```csharp
_validatorTypeInfo = _validationOptions != null
    && _validationOptions.TryGetValidatableTypeInfo(_editContext.Model.GetType(), out var typeInfo)
    ? typeInfo : null;
...
if (_validatorTypeInfo is not null) ValidateFormWithValidatableInfo(_validatorTypeInfo);
else                                ValidateFormWithValidator();   // Validator.TryValidateObject
```

The localizer lives entirely inside the generated path. The `Validator.TryValidateObject`
fallback does not localize. `AddValidation()` on its own changes nothing.

`ValidationsGenerator.Initialize` discovers exactly two things: `[ValidatableType]`-marked
types, and minimal-API endpoint parameter types. There is no Blazor `EditForm`-model
discovery.

### 2.2 `[ValidatableType]` needs no package reference in a Razor project

`Sdk.Razor.CurrentVersion.targets:615` sets `GenerateEmbeddedValidatableTypeAttribute=true`
for **any project with `.razor` files**, emits an internal
`Microsoft.Extensions.Validation.Embedded.ValidatableTypeAttribute`, and adds a global
`using` for that namespace. So a bare `[ValidatableType]` compiles and is discovered with no
`PackageReference` and no `using`.

> **Do not add `using Microsoft.Extensions.Validation;`** to a file that uses
> `[ValidatableType]` — the framework's copy and the embedded copy collide with
> `CS0104: 'ValidatableType' is an ambiguous reference`. Hit on the first build of the
> scratch project.

`[SkipValidation]` has no embedded copy, so it needs the fully-qualified name
`[Microsoft.Extensions.Validation.SkipValidation]`.

### 2.3 Models in `.razor` `@code` are silently skipped — `.razor.cs` works

Three declaration sites, one build, `ValidatableInfoResolver.g.cs` inspected:

| Declaration site | Discovered? |
|---|---|
| plain `.cs` file, top-level class | **yes** |
| `.razor.cs` code-behind, **nested inside the component partial** | **yes** |
| `.razor` `@code` block, nested inside the component | **no** — build succeeded, 0 warnings |

`.razor` files are compiled *by* the Razor source generator, and one source generator cannot
observe another's output. There is no diagnostic; the form just keeps rendering English.
Microsoft's own Blazor validation test models live in `.cs` files for this reason.

**The model does not need to leave the component** — nesting inside the partial in a
`.razor.cs` is enough. That is the whole refactor.

### 2.4 `AddValidation()` must be called in *every* project that declares models

Verified with a two-project setup: a `[ValidatableType]` model in a referenced Razor class
library that does **not** call `AddValidation()` is not discovered by the consuming app's
generator, and the library emits no resolver at all.

With `AddValidation()` called in both, resolvers accumulate correctly:

```
resolvers: 4
  VTest.Components.RazorModel2+FormModel -> YES
  CsFormModel                            -> YES
  VLib.LibModel+FormModel                -> YES
```

For us that means one call in `UI.Blazor`'s module and one in `UI.Blazor.App`'s.

### 2.5 `InvariantGlobalization` is a non-issue

The pipeline calls `LocalizerProvider(type, factory)` and indexes the returned
`IStringLocalizer`. `AppStringLocalizer` already *is* an `IStringLocalizer<Strings>` over
the embedded JSON catalogs, selecting the language from `LanguageUI.UILanguage`. Verified
with `CurrentCulture` and `CurrentUICulture` both pinned to `InvariantCulture`:

```
[ru] Заполните поле «Псевдоним». | Ссылка слишком короткая.
[en] The Nickname field is required. | Custom link is too short.
```

Switching languages between the two lines is a field assignment on the fake catalog — i.e.
app state, exactly like `LanguageUI`.

### 2.6 App attributes with several messages can self-localize

`AliasIdAttribute` and `PhoneOrEmailAttribute` each produce more than one sentence, so an
attribute-type key can't distinguish them, and the framework keys on the *attribute's*
`ErrorMessage`, not the *result's*. The working answer: resolve inside `IsValid` from the
`ValidationContext`, which carries the service provider on both paths.

```csharp
var key = s.Length < 5 ? "Validation_AliasTooShort" : "Validation_AliasInvalidCharacters";
var localizer = ctx.GetService(typeof(IStringLocalizer)) as IStringLocalizer;
var text = localizer?[key] is { ResourceNotFound: false } ls ? ls.Value : key;
return new ValidationResult(text, …);
```

The key provider returns `null` for these attributes, so §1.1's `string.IsNullOrEmpty(lookupKey)`
branch passes the already-localized `result.ErrorMessage` straight through. Verified.

### 2.7 `FormModel<T>.Base` doubles every message unless skipped

This one is ours alone and would have been a nasty surprise. `FormModel<TFormModel>` exposes
`public TFormModel Base { get; set; }` — a complex property **of the model's own type** —
and `CopyToBase()` populates it during form init. The new pipeline recurses into complex
properties whose types are validatable, so every message appears twice:

```
[ru] Заполните поле «Псевдоним». | Ссылка слишком короткая. | Заполните поле «Псевдоним». | Ссылка слишком короткая.
```

One attribute on `UI.Blazor/FormModel.cs` fixes it, verified:

```csharp
[Microsoft.Extensions.Validation.SkipValidation]
public TFormModel Base { get; set; } = null!;
```

`FormModel.Fields` (`FormFieldInfo[]`) is not affected — `FormFieldInfo` isn't
`[ValidatableType]`, so it is never recursed into.

### 2.8 `ErrorMessage` vs. the key provider — precedence is *inverted* between the two API shapes

The one place where §1's "same mechanism, fewer moving parts" is untrue, and the most likely
source of a silent regression. Both behaviours verified.

| | preview.6 (installed) | main |
|---|---|---|
| Docs | "the delegate is invoked for **every** attribute and **takes precedence over** `ValidationAttribute.ErrorMessage`" | "when a validator specifies an explicit message, that message is used as the lookup key and **the provider is not consulted**" |
| `[Required(ErrorMessage = "Explicit_Key")]` | provider wins → `Validation_Required` | `ErrorMessage` wins → `Explicit_Key` |

Verified on preview.6: a property marked `[Required(ErrorMessage = "Explicit_Key")]`
rendered the *convention* key's text, not `Explicit_Key`'s.

Two consequences, both live in our code today:

**(a) `DeleteAccountModal` would silently change message.** Lines 83-84 carry
`[Required(ErrorMessage = "Delete confirmation is required")]` and
`[RegularExpression("^DELETE$", ErrorMessage = "Please enter DELETE to confirm")]`. Under
preview.6 a naive key provider overrides both with `Validation_Required` /
`Validation_RegularExpression`.

**(b) `[EmailAddress]`, `[Phone]` and `[Url]` pre-populate `ErrorMessage` in their
constructors** — verified:

```
EmailAddressAttribute.ErrorMessage = "The {0} field is not a valid e-mail address."
PhoneAttribute.ErrorMessage        = "The {0} field is not a valid phone number."
UrlAttribute.ErrorMessage          = "The {0} field is not a valid fully-qualified http, https, or ftp URL."
RequiredAttribute.ErrorMessage     = null
```

So "the developer set a message" cannot be tested as `ErrorMessage != null`. On preview.6
these three happen to work (the provider wins anyway); on `main` they would silently fall
back to English, because their ctor-populated default becomes the lookup key and misses.
The same trap was hit independently on the `feat/localizatiion-via-resx` branch.

**The fix makes both versions behave identically — adopt it from the start:**

1. **Put the catalog key in `ErrorMessage`** wherever a site needs its own message, rather
   than an English sentence. `[Required(ErrorMessage = "Validation_DeleteConfirmationRequired")]`.
2. **The key provider defers** when `ErrorMessage` is set to something other than the
   attribute type's constructor default:

   ```csharp
   var attr = ctx.Attribute;                       // ctx.ValidatorType on main
   if (IsAppOwned(attr))                           // self-localizing, §2.6
       return null;
   if (!attr.ErrorMessage.IsNullOrEmpty() && attr.ErrorMessage != DefaultErrorMessage(attr.GetType()))
       return null;                                // developer-set key — let it be the lookup key
   return ValidationKeys.Prefix + attr.GetType().Name.TrimSuffix("Attribute");
   ```

   `DefaultErrorMessage` is a small `Dictionary<Type, string?>` seeded by constructing each
   ctor-populating BCL attribute once.
3. **Prefer our own attributes over the ctor-populating BCL ones.** We already have
   `[Email]` and `[PhoneNumber]`; the single BCL `[EmailAddress]` usage
   (`Onboarding/EmailStep.razor:97`) should become `[Email]`, which is self-localizing and
   sidesteps the divergence entirely. `[Phone]` and `[Url]` have no UI usage.

Verified with the deferring provider on preview.6 — every case lands where it should:

```
[en] The Nickname field is required.        <- [Required], convention key
   | EXPLICIT-ERRORMESSAGE-WON Nickname     <- [Required(ErrorMessage = "Explicit_Key")]
   | Custom link is too short.              <- app-owned, self-localized
   | EN-KEY-HIT email Email                 <- [EmailAddress], ctor default ignored
   | EN-KEY-HIT phone Phone                 <- [Phone], ctor default ignored
```

### 2.9 `ASP0029`

`IValidatableTypeInfo` and friends carry `[Experimental("ASP0029")]` in preview.6 — an error
by default, suppressed with `<NoWarn>$(NoWarn);ASP0029</NoWarn>`. We only touch it if we
name those types directly, which the plan doesn't. The marking is gone from
`src/Validation/src` on `main`.

## 3. Current surface

### 3.1 Validators

| Component | Sites |
|---|---|
| `<DataAnnotationsValidator/>` (stock) | 24 |
| `<AsyncDataAnnotationsValidator/>` (ours) | 4 — `OwnAccountEditorModal`, `Onboarding/PhoneStep`, `SignIn/Modal/ProviderSelectStep`, `Pages/TotpTestPage` |

Both are live. The 4 ours-sites must move to `<DataAnnotationsValidator/>`, since only the
stock component reads `IOptions<ValidationOptions>`. `.NET 11`'s `EditContext.ValidateAsync`,
`IsValidationPending`, `IsValidationFaulted` and `RegisterAsyncFieldValidator` cover
everything `EditContextAsyncValidator` was written for, which is what its standing
`TODO(FC)` anticipated.

### 3.2 Attributes that reach a user

| Attribute | UI sites | Extra `{n}` args | Key |
|---|---|---|---|
| `[Required]` | ~19 | none | `Validation_Required` (convention) |
| `[Required(ErrorMessage = …)]` | 1 | none | `ErrorMessage` becomes the key — §2.8(a) |
| `[EmailAddress]` (BCL) | 1 | none | **replace with our `[Email]`** — §2.8(3) |
| `[RegularExpression]` (BCL) | 1 | pattern | `ErrorMessage` becomes the key — §2.8(a) |
| `[Email]` (ours) | 2 | none | self-localized (§2.6) |
| `[PhoneNumber]` (ours) | 3 | none | self-localized |
| `[PhoneOrEmail]` (ours) | 1 | none | self-localized |
| `[AliasId]` (ours) | 3 | none | self-localized |

`[Range]` and `[StringLength]` exist only in `MLSearch.Service/Module/MLSearchSettings.cs`
and `Users.Service/Db/DbUserSession.cs` — settings binding and EF column metadata, never
rendered. `[MinLength]`/`[MaxLength]` have had no UI usage since `994701d035`.

**Every UI-facing message is `{0}`-only**, so we implement no formatters and the preview.5→
main formatter reshape (§1) costs us nothing.

All four app-owned attributes are used **exclusively in UI form models** — no server-side
model validation depends on their English text.

The three sites where §2.8 bites, all in two files:

- `Onboarding/EmailStep.razor:97` — `[Required, EmailAddress]` → `[Required, Email]`
- `DeleteAccountModal.razor:83` — `ErrorMessage = "Delete confirmation is required"` →
  `"Validation_DeleteConfirmationRequired"`
- `DeleteAccountModal.razor:84` — `ErrorMessage = "Please enter DELETE to confirm"` →
  `"Validation_DeleteConfirmationInvalid"`

### 3.3 The 17 models to move

| Project | File | Model(s) |
|---|---|---|
| UI.Blazor | `Pages/TotpTestPage.razor` | `Model` |
| UI.Blazor | `Components/SignIn/Modal/ProviderSelectStep.razor` | `StepModel` |
| UI.Blazor.App | `Pages/AdminCopyChatToPlacePage.razor` | `FormModel` |
| UI.Blazor.App | `Components/Onboarding/{PhoneStep,AvatarStep,TimeZoneStep,EmailStep}.razor` | `Model` ×4 |
| UI.Blazor.App | `Components/NewThread/NewThreadModal.razor` | `FormModel` |
| UI.Blazor.App | `Components/Settings/EmailSettings.razor` | `EmailFormModel` |
| UI.Blazor.App | `Components/ChatSettings/ChatSettingsStartModalPage.razor` | `FormModel` |
| UI.Blazor.App | `Components/PlaceSettings/{CopyChatToPlaceModal,PlaceSettingsOwnerModalPage,PlaceSettingsEditTypeModalPage}.razor` | `FormModel` ×3 |
| UI.Blazor.App | `Components/NewPlace/NewPlaceModalProps.razor` | `FormModel` |
| UI.Blazor.App | `Components/NewChat/NewChatModalProps.razor` | `FormModel` |
| UI.Blazor.App | `Components/{DeleteAccountModal,OwnAvatarEditorModal,OwnAccountEditorModal}.razor` | `FormModel` ×3 |

`Components/ChatSettings/EditChatTypeModalPage` already keeps its `FormModel` in a
`.razor.cs` — no move needed, `[ValidatableType]` only.

The repo has 9 `.razor.cs` code-behinds already, so the pattern and the build wiring exist.

### 3.4 Display names

12 `[Display(Name = …)]` sites, 6 distinct names, 4 of which have drifted from the
`FormSection Label` the user actually sees (`"User link"` vs `"Short name"`, `"Custom link"`
vs `"Short name"` ×2, and one localized-label/English-message pair). §1.5 of the superseded
plan has the table.

## 4. Reuse

### 4.1 Existing abstractions to reuse

- **`AppStringLocalizer`** (`UI.Blazor.App/Services/`) — `IStringLocalizer<Strings>` over the
  embedded JSON catalogs, keyed on `LanguageUI`. This *is* the localizer; §2.5 verifies the
  shape works unchanged. Only an `IStringLocalizerFactory` shim is new.
- **`StringCatalog`**, **`Strings.*.json`** — unchanged.
- **`MessageIndex` / `MessageMatch`** — kept, reduced to the `Error_` prefix for
  `docs/plans/server-strings-localization.md`. Loses `ValidationPrefix`, `FieldPrefix`,
  `GetFieldKey`, `MessageTemplate`, `PlaceholderRe` and `MessageMatch.HasFieldArg`.
- **`LocalizedMessage.razor`** — stays as the tier-3 AI fallback for text that arrives
  already-English (server errors, uncatalogued strings). It stops being the validation
  mechanism and becomes the safety net it was always described as. Its `FieldLabel`
  parameter goes away.
- **`ValidationContextExt.Error`** (`Core/Validation/`) — the existing helper our attributes
  use; extended to take a key and resolve it (§2.6).
- **`FormSection`** — gains a `Label` default from the resolved display name (§5.4);
  everything else unchanged.
- **Stock `<DataAnnotationsValidator/>`**, `EditContext.ValidateAsync`,
  `RegisterAsyncFieldValidator` — replace `AsyncDataAnnotationsValidator` /
  `EditContextAsyncValidator` outright.

### 4.2 Reusability of new components

Only two new things, both tiny:

1. **`AppStringLocalizerFactory`** — `IStringLocalizerFactory` returning `AppStringLocalizer`
   for any resource type. Placement: next to `AppStringLocalizer` in
   `UI.Blazor.App/Services/`. It is meaningless outside that layer (it exists to bridge to
   `AppStringLocalizer`), so **local, not shared**.
2. **The key convention** — `"Validation_" + attributeTypeName.TrimSuffix("Attribute")`. It
   is referenced by the DI config and by the tests, and `server-strings-localization.md`
   will want the same convention for command validation errors. Placement options:
   `ActualChat.Core` (`ActualChat.Validation`, next to `PhoneOrEmailAttribute` and
   `ValidationContextExt`) vs. `UI.Blazor.Resources` (next to `MessageIndex`).
   **Recommend `ActualChat.Core`** — it is a property of the attributes, which live there,
   and Core can reference `Microsoft.Extensions.Localization.Abstractions` without pulling
   in UI. One `static class ValidationKeys` with the convention method and the shared
   prefix constant.

No new validator, no new resolver, no new message index. The net line count is negative.

## 5. The change

### 5.1 DI (one file per project)

```csharp
// UI.Blazor.App module
services.AddSingleton<IStringLocalizerFactory, AppStringLocalizerFactory>();
services.AddValidation();
services.AddValidationLocalization(options => {
    options.LocalizerProvider = (_, factory) => factory.Create(typeof(Strings));
    options.ErrorMessageKeyProvider = ValidationKeys.ForAttribute;   // §2.8 — defers, not overrides
});

// UI.Blazor module — AddValidation() only; the options above are shared
services.AddValidation();
```

`AddValidation()` in both is what §2.4 requires. Keeping every localization option in one
place is what bounds the preview-churn risk (§8) — on `main`'s shape this collapses into the
`AddValidation()` callback and `ErrorMessageKeyProvider` becomes `MessageKeyProvider`.

`ValidationKeys.ForAttribute` must implement the **deferring** rule of §2.8, not a plain
`"Validation_" + name` — otherwise `DeleteAccountModal`'s two custom messages are silently
overridden on preview.6, and `[EmailAddress]`-style attributes silently revert to English on
main.

### 5.2 Models

Per model: move the class from the `@code` block to a `.razor.cs` code-behind (nested inside
the component partial, unchanged otherwise) and mark it `[ValidatableType]`. Plus one line
in `UI.Blazor/FormModel.cs` for `[SkipValidation]` on `Base` (§2.7), and the three
attribute-level fixes listed at the end of §3.2.

### 5.3 Catalog

`Messages.en.json`'s reverse templates go away; the app-owned sentences become forward keys.
Recommended landing place is `Strings.*.json`, leaving `Messages.*.json` to mean
"reverse-indexed" and nothing else — `AppStringLocalizer.LoadAll` merges both, so lookups
are unaffected either way.

```jsonc
{
  "Validation_Required": "The {0} field is required.",
  "Validation_EmailAddress": "The {0} field is not a valid e-mail address.",
  "Validation_Email": "Email address is invalid.",
  "Validation_PhoneInvalidCharacters": "Phone number contains invalid characters.",
  "Validation_PhoneTooShort": "Phone number is too short.",
  "Validation_PhoneTooLong": "Phone number is too long.",
  "Validation_PhoneOrEmail": "Enter a phone number or email address.",
  "Validation_AliasTooShort": "Custom link is too short.",
  "Validation_AliasInvalidCharacters": "Custom link should contain only 0-9, a-Z, - and _.",
  "Validation_DeleteConfirmationRequired": "Delete confirmation is required",
  "Validation_DeleteConfirmationInvalid": "Please enter DELETE to confirm",

  "Field_Name": "Name",
  "Field_UserLink": "User link",
  "Field_Phone": "Phone",
  "Field_Email": "Email",
  "Field_TimeZone": "Time zone",
  "Field_PhoneOrEmail": "Phone or email"
}
```

The English values are ordinary translations now, not byte-match obligations — nothing
reverse-matches them. `Validation_Required` keeps `{0}` because `string.Format` fills it;
that is forward formatting, not reverse parsing.

`Validation_Required_Format`, `Validation_MinLength_Format` and
`Validation_EmailAddress_Format` — the three copies of .NET's own resource text — are
deleted across all 14 languages.

### 5.4 Display names

`{0}` is now resolved at validation time, so `[Display(Name = …)]` is load-bearing — which is
exactly the drift risk §3.4 documents. The fix is to make it the *single* source rather than
a second one:

- `[Display(Name = "Field_UserLink")]` — a catalog key, resolved through `AppStringLocalizer`
  exactly as the framework's `LiteralDisplayName` does (verified in §2.5: `Field_Nick` →
  `Псевдоним`).
- `FormSection.Label` **defaults** to that resolved display name when no `Label` is passed.
  Sites needing a different label from the field name keep passing one; the rest stop
  repeating it, and the four drifted pairs collapse to one string each.

### 5.5 Deletions

- `AsyncDataAnnotationsValidator`, `EditContextAsyncValidator` (its `TODO(FC)` resolved), and
  the 4 usages → stock `<DataAnnotationsValidator/>`.
- `MessageIndex`: `ValidationPrefix`, `FieldPrefix`, `GetFieldKey`, `MessageTemplate`,
  `PlaceholderRe`, `MessageMatch.HasFieldArg`.
- `MessageLocalizer.TryMessage`'s `fieldLabel` argument; `LocalizedMessage.FieldLabel`.
- `FormSection.ValidationMessage` reverts from `RenderFragment<string>` to `RenderFragment`,
  subject to §10.2.
- `Validation_*_Format` catalog entries × 14 languages.
- The test that pins .NET's `[Required]` / `[MinLength]` / `[EmailAddress]` wording — that
  contract no longer exists.

## 6. Order of work

1. `[SkipValidation]` on `FormModel<T>.Base` — independent, and the doubling bug is invisible
   until step 4.
2. `AppStringLocalizerFactory` + `ValidationKeys` + DI wiring in both projects.
3. Catalog: forward keys and `Field_*` entries, 14 languages.
4. Models: move to `.razor.cs`, `[ValidatableType]`, `[Display(Name = "Field_*")]`. One
   component first (`OwnAccountEditorModal` — it has `[Required]`, an app attribute, and a
   drifted display name), verify in the browser, then the rest.
5. App attributes self-localize (§2.6).
6. Swap the 4 `AsyncDataAnnotationsValidator` sites to `<DataAnnotationsValidator/>`; delete
   the components.
7. Trim `MessageIndex`, `LocalizedMessage`, `FormSection`; delete dead catalog entries.
8. Tests (§7).

Steps 1-3 are inert on their own — nothing changes until a model is marked in step 4, so the
branch stays shippable throughout.

## 7. Tests

- **`ValidationLocalizationTest`** (replaces `ValidationMessageLocalizationTest`) — for every
  attribute type used in a UI form model and every key our attributes emit, assert the key
  resolves in all 14 languages and `string.Format` over the template with the right arg count
  doesn't throw.
- **Discovery guard** — the silent-skip failure mode of §2.3 needs a test, not a convention:
  reflect over every `EditForm` model type in `UI.Blazor` + `UI.Blazor.App` and assert
  `ValidationOptions.TryGetValidatableTypeInfo` returns `true` for each. This is the single
  most valuable test here; without it a model moved back into `@code`, or a missing
  `[ValidatableType]`, reverts that form to English with no signal.
- **Display-name test** — every `[Display(Name = …)]` on a form model resolves to an existing
  `Field_*` key.
- **No-duplicate-message test** — pins §2.7: a `FormModel<T>` subclass with `Base` populated
  produces each message exactly once.
- **No-English-leak test** — force a failure per attribute per form under a non-English
  language and assert no message equals its English catalog value. `AppLocalizationTest`'s
  "invoke every member" pattern is the model.

## 8. Risks

| Risk | Mitigation |
|---|---|
| preview.7 reshapes the API (§1) | ~10 lines, all inside the one DI file of §5.1. Keys, catalog, models and tests are unaffected |
| A model is missed or moved back into `@code` → that form silently reverts to English | The §7 discovery guard is the whole answer; it fails the build, not the user |
| `Base` recursion doubles messages | §2.7, one attribute, plus the §7 test |
| Recursive validation reaches other complex properties we didn't intend | Only `[ValidatableType]` types are recursed into, and we mark exactly the form models; the discovery guard doubles as an inventory |
| 24 forms change validator component | Same DataAnnotations semantics; QA pass over the modal forms, one component verified in-browser first (step 4) |
| `ASP0029` | `<NoWarn>`; not needed unless we name the experimental types |
| Key-provider precedence flips on preview.7 (§2.8) | The deferring provider is written to give identical results under both rules; the repro's `Nick2` case is the canary |
| A BCL attribute with a ctor-populated `ErrorMessage` is added later and silently reverts to English on main | `DefaultErrorMessage` table + the §7 no-English-leak test |

## 9. Reproduction

Kept in the repo at **`tmp/validation-l10n-repro/`** (gitignored, survives a reboot) with a
`README.md` mapping each file to the section it proves. `cd tmp/validation-l10n-repro/vtest
&& dotnet run` reprints the §2.4/§2.5 output. Re-run it against preview.7 before starting
step 4 of §6.

Should that folder ever be lost, the setup is: `vtest` (`Microsoft.NET.Sdk.Web`, `net11.0`,
`OutputType=Exe`, `global.json` pinned to `11.0.100-preview.6.26359.118`,
`EmitCompilerGeneratedFiles=true` + `<Compile Remove="gen/**" />`) containing the same model
declared three ways — `.razor` `@code`, `.razor.cs` nested partial, plain `.cs`; plus `vlib`
(`Microsoft.NET.Sdk.Razor`, `FrameworkReference Microsoft.AspNetCore.App`) holding a fourth
in a referenced project. `Program.cs` registers a fake `IStringLocalizerFactory` whose
language is a static field, calls `AddValidation()` + `AddValidationLocalization()`, creates
an `EditContext` over the model, calls `EnableDataAnnotationsValidation(sp)` and prints
`GetValidationMessages()` per language. Read the discovered set from
`gen/Microsoft.Extensions.Validation.ValidationsGenerator/…/ValidatableInfoResolver.g.cs`.

Without `<Compile Remove="gen/**" />` the emitted files are compiled twice and the build
fails on duplicate `InterceptsLocationAttribute` — a red herring worth not rediscovering.

## 10. Decisions and open questions

**Decided — take the preview.** The repo is already on a preview SDK, the churn exposure is
one DI file (§5.1), and the 17-model move is work we owe regardless. Do not wait for GA.

Still open, all cheap and none blocking the start:

1. **`Validation_*` / `Field_*` in `Strings.*.json` or `Messages.*.json`?** §5.3 recommends
   `Strings`, leaving `Messages` to mean "reverse-indexed" only. `AppStringLocalizer.LoadAll`
   merges both, so lookups work either way.
2. **Does `AliasValidationMessage` still need the field label** for its own non-validation
   hints ("This link is available.")? If not, §5.5's `FormSection` deletion is larger.
3. **`TotpTestPage`, `AudioBlobDownloadTestPage`, `AdminCopyChatToPlacePage`** are dev/admin
   pages. Include them for uniformity — and because the §7 discovery guard would otherwise
   need an exclusion list — or leave them unlocalized?

## 11. Resume here

**State as of 2026-08-11:** research complete and verified, no production code touched.
`git status` shows only this plan as untracked; `tmp/validation-l10n-repro/` is gitignored.
Nothing has been committed for this plan.

**Start at §6 step 1.** In order:

0. Re-run `tmp/validation-l10n-repro/vtest` first (`dotnet run`) — it re-establishes every
   §2 finding in ~1 minute and tells you immediately whether a newer SDK changed the
   precedence rule of §2.8.
1. `src/dotnet/UI.Blazor/FormModel.cs` — add
   `[Microsoft.Extensions.Validation.SkipValidation]` to `FormModel<TFormModel>.Base`
   (§2.7). Do **not** add `using Microsoft.Extensions.Validation;` (§2.2, `CS0104`).
2. `AppStringLocalizerFactory` next to `AppStringLocalizer` in `UI.Blazor.App/Services/`;
   `ValidationKeys` in `Core/Validation/` (§4.2); DI wiring per §5.1 in
   `UI.Blazor/Module/BlazorUIModule.cs` and `UI.Blazor.App/Module/BlazorUIAppModule.cs`.
3. Catalog per §5.3, 14 languages.
4. The three §3.2 attribute fixes (`EmailStep`, `DeleteAccountModal` ×2) — small, and they
   are the ones that regress silently if forgotten.
5. First model: `UI.Blazor.App/Components/OwnAccountEditorModal.razor` — it carries
   `[Required]`, an app-owned attribute (`[AliasId]`), and one of the drifted display names
   (`Label="User link"` vs `[Display(Name = "Short name")]`). Move `FormModel` (line ~238)
   into `OwnAccountEditorModal.razor.cs`, mark `[ValidatableType]`, switch `[Display]` to
   `Field_*` keys, and verify in the browser before touching the other 16.

Then §6 steps 5-8 as written.

**Context worth not re-deriving:**

- Ref pack read during research:
  `/usr/local/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/11.0.0-preview.6.26359.118/ref/net11.0/`
  — `Microsoft.Extensions.Validation.xml` and `Microsoft.Extensions.Validation.Localization.xml`
  carry the full doc comments for the installed shape.
- `main`-branch sources compared against: `dotnet/aspnetcore` sha `330e89cc`, files
  `src/Validation/src/ValidationOptions.cs`, `src/Validation/gen/ValidationsGenerator.cs`,
  `src/Components/Forms/src/EditContextDataAnnotationsExtensions.cs`,
  `src/Components/Endpoints/src/Forms/DataAnnotationsLocalizer.cs`, and the generator
  snapshot `src/Validation/test/…/snapshots/ValidationsGeneratorTests.CanValidateClassTypesWithAttribute#ValidatableInfoResolver.g.verified.cs`
  (the readable version of what the generator emits).
- Razor SDK rule injecting the embedded attribute:
  `/usr/local/share/dotnet/sdk/11.0.100-preview.6.26359.118/Sdks/Microsoft.NET.Sdk.Razor/targets/Sdk.Razor.CurrentVersion.targets:615`.
- Latest published packages are `11.0.0-preview.6` for both
  `Microsoft.Extensions.Validation` and `…Validation.Localization`; `main` is ahead of both,
  so §1's right-hand column is unreleased. Re-check with
  `dotnet package search Microsoft.Extensions.Validation.Localization --exact-match --prerelease`
  — if a preview.7 exists, §1 and §5.1 need the rename pass before step 2.
- A parallel experiment exists on branch **`feat/localizatiion-via-resx`** (off `dev`,
  2026-08-08) that reached the same forward-key design by hand — `LocalizingValidator` +
  `ValidationMessageLocalizer.Describe` with `Validation_<Attr>` / `Field_<Property>`
  conventions, and `tests/UI.Blazor.UnitTests/LocalizingValidatorTest.cs`. Worth reading
  before writing §7's tests; its two recorded traps are the ctor-populated `ErrorMessage`
  (confirmed here, §2.8) and `AsyncValidationAttribute`'s sync half. It also documents that
  `ActualChat.Validation` is a **global using**, so any same-named attribute is ambiguous
  with the DataAnnotations one — relevant if we ever wrap a BCL attribute.
- The superseded plan is `docs/plans/validation-messages-localization.md`; its §1 inventory
  (message-by-message, with file:line) is still accurate and is the source for §5.3's key
  list. Only its *mechanism* is replaced.
