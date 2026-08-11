# Validation Localization — Hybrid: Standard Attributes + Named-Placeholder Reverse Index

**Date:** 2026-08-11
**Branch:** `feat/3721-app-localization` (supersedes the validation half of
`docs/plans/validation-messages-localization.md`)
**Status:** Implemented — §5 for what landed, §7 for what's left

## 0. TL;DR

Validation messages take **two routes**, chosen by who owns the attribute:

| Attribute | Produces | Localized by |
|---|---|---|
| BCL — `[Required]`, `[EmailAddress]`, `[MinLength]`, … | its normal English sentence | `MessageIndex` reverse-matches it back to a key |
| Ours — `[Email]`, `[PhoneNumber]`, `[PhoneOrEmail]`, `[AliasId]` | a `Validation_*` **key** | resolved directly, no matching needed |

Standard attributes stay standard — no `[AppRequired]` wrappers. Our own attributes skip the
English round-trip entirely, because reverse-matching text we control buys nothing and adds a
collision surface. Both routes converge in `LocalizedMessage`, which runs inside the circuit and
therefore uses **that user's** `IStringLocalizer`.

Placeholders are **named**: `"The {field} field is required."` rather than `{0}`. `MessageIndex`
derives `^The (.+?) field is required\.$` from it and returns `{ field: "Name" }`, so the renderer
knows which captured value is the field name (replaced by the form label) and which are values
(passed through untouched). One human-readable string per language, no hand-written regexes.

The whole byte-match obligation is now **three English strings** — `Validation_Required_Format`,
`Validation_MinLength_Format`, `Validation_EmailAddress_Format` — pinned by
`ValidationMessageLocalizationTest`, which fails the build rather than the user.

## 1. Why not the .NET 11 validation pipeline

Not because it's broken — because of *when* it resolves the localizer, and only on the SDK we
have installed.

**The framework's model is ambient culture.** `IStringLocalizer` has no culture parameter at all;
`ResourceManagerStringLocalizer` reads `CultureInfo.CurrentUICulture` on **every** lookup
(`GetStringSafely`, `Localization/src/ResourceManagerStringLocalizer.cs:161`), is documented
thread-safe, and its factory is registered **singleton** on purpose
(`LocalizationServiceCollectionExtensions.cs:57`). A shared localizer serving many users in many
languages is correct by design — multi-user Blazor Server was never at risk.

**Our localizer breaks that assumption**, not the interface: `AppStringLocalizer` takes its
language from the scoped `LanguageUI` (the app is `InvariantGlobalization`, so `CultureInfo`
can't be the key). The instance therefore carries per-user state, and instance sharing stops
being safe.

**preview.6 shares the instance.** `ValidationLocalizationSetup.cs:11-16` injects
`IStringLocalizerFactory` once and bakes it into the singleton `ValidationOptions.Localizer`;
`DefaultValidationLocalizer` then hands that same captured factory to `LocalizerProvider` on
every call. Measured in `tmp/validation-l10n-repro/`: **every registration lifetime fails**, and
the DI error names the cause —

```
--- IStringLocalizerFactory registered as Scoped    --- Cannot resolve scoped service … from root provider.
--- IStringLocalizerFactory registered as Transient --- Cannot resolve scoped service … from root provider.
--- IStringLocalizerFactory registered as Singleton --- Cannot resolve scoped service … from root provider.
```

It throws only because the repro sets `ValidateScopes = true`; in Release it would silently build
one root-level localizer and share it across every user.

**`main` already fixes it.** `Validation/gen/Templates/ValidatableInfo.cs:83` resolves the factory
per validation from `context.ServiceProvider`, which
`EditContextDataAnnotationsExtensions.CreateFormValidateContext()` sets to the circuit's scoped
provider. So on a future preview our scoped localizer would work through the pipeline unchanged.

**Two exits exist if we ever want the pipeline:** wait for `main`'s shape to ship, or adopt
ambient culture — `RequestLocalizationOptions` gates `SupportedCultures` and `SupportedUICultures`
independently, so `CurrentCulture` can stay `InvariantCulture` (formatting untouched, which is
what `Directory.Build.props` actually protects) while `CurrentUICulture` varies per user. That
needs `PredefinedCulturesOnly=false`, already proven on `feat/localizatiion-via-resx`.

Neither is needed today, and neither invalidates the keys, the catalog or the tests.
`MessageIndex` and `Messages.en.json` carry `TODO(FC)` notes pointing here, so whoever bumps the
SDK sees that preview.7 is the release to re-check.

## 2. The design

### 2.1 Two routes, one render site

```
[Required]  → "The Name field is required."  → MessageIndex → Validation_Required_Format + {field: "Name"}
[Email]     → "Validation_EmailInvalid"      → resolved as a key
                                                     ↓
                                          LocalizedMessage (in the circuit)
                                          {field} ← FormSection.Label
```

`LocalizedMessage` tries, in order: `TryKey` (our attributes), `TryMessage` (reverse index), then
the AI fallback for anything uncatalogued. Ordering matters — without `TryKey` first, a key would
fall through to AI translation.

### 2.2 Named placeholders

`MessageIndex` accepts `{name}` and builds the regex from it, so one readable string per language
serves both directions:

```jsonc
// Messages.en.json — must byte-match .NET's own wording
"Validation_MinLength_Format": "The field {field} must be a string or array type with a minimum length of '{min}'."
// Messages.ru.json — ordinary translation, same names, free word order
"Validation_MinLength_Format": "Поле «{field}» должно содержать не менее {min} символов."
```

`{field}` is the one placeholder a form label may replace (`MessageIndex.FieldArg`); everything
else passes through with its captured value. That replaces the old `HasFieldArg` convention
("arg 0 of a `Validation_` key is the field name"), which couldn't describe `[Range]`-style
messages. Adjacent placeholders and repeated names are rejected at construction.

### 2.3 The field name is the form label

Only `{field}` needs substituting, and it comes from `FormSection.Label` — the string the user
actually sees — falling back to the `Field_*` catalog when a section has no label. `[Display]` is
deliberately **not** the source: four of eleven sites had already drifted from their labels.

This is why the labels themselves had to be localized (§5.4): an English label would put an
English noun inside a Russian sentence.

## 3. Reuse

- **`MessageIndex` / `MessageLocalizer` / `LocalizedMessage`** — kept; gained named placeholders,
  `TryKey`, and `MessageIndex.Format`.
- **`AppStringLocalizer`, `StringCatalog`, `Strings.*.json`** — unchanged.
- **`ValidationContextExt.Error`** — unchanged; our attributes pass a key instead of a sentence.
- **`EditContextAsyncValidator` / `AsyncDataAnnotationsValidator`** — unchanged.
- **New:** `ValidationKeys` (`ActualChat.Core`, `ActualChat.Validation`) — the keys our own
  attributes report, in one place so the test can assert all 14 languages.

## 4. Catalog

`Messages.*.json` (reverse-indexed) holds exactly six entries: the three BCL templates, the two
`DeleteAccountModal` literals, and `Field_PhoneOrEmail`. Everything our attributes report lives in
`Strings.*.json` as ordinary forward keys. `MessageIndex` rejects any key outside
`Validation_` / `Error_` / `Field_`, and `CatalogsShouldNotShareKeys` keeps the two disjoint.

`Validation_*` keys are excluded from `AppStringsMembersMatchEnglishKeysExactly`: they're resolved
by key from Core, which can't reference `AppStrings`.

## 5. What landed

1. `MessageIndex` — named placeholders, `FieldArg`, `Format`, named-arg `MessageMatch`.
2. `MessageLocalizer.TryKey` + named substitution in `TryMessage`; `LocalizedMessage` tries the
   key first.
3. `ValidationKeys`; `Validators.Email`/`.Phone` return keys; `Email`, `PhoneNumber`,
   `PhoneOrEmail`, `AliasId` report them.
4. Catalog split per §4, all 14 languages, `{0}` → `{field}`/`{min}`.
5. Form labels localized so the field name translates: `Form_*` (14 keys) across
   `ChatSettingsStartModalPage`, `NewThreadModal`, `PlaceSettingsOwnerModalPage`,
   `CopyChatToPlaceModal`, `NewPlaceModalProps`, `NewChatModalProps`, `OwnAvatarEditorModal`,
   `DeleteAccountModal`, `EmailStep`, `AvatarStep`, `TimeZoneStep`, `PhoneStep`,
   `EditChatTypeModalPage`, `PlaceSettingsEditTypeModalPage`. `DeleteAccountModal`'s own prose
   was localized too (`DeleteAccount_*`, 12 keys).
6. Tests — `ValidationMessageLocalizationTest` covers both routes; `MessageIndexTest` moved to
   named args; `AsyncValidationTest` expects keys.

Rejected on the way: `[AppRequired]` / `[AppRegularExpression]` wrappers (built, then removed —
standard attributes are worth the three-string byte-match obligation).

## 6. Reproduction

`tmp/validation-l10n-repro/` (gitignored, own README). `RunScopeTest` and `FactoryLifetimes`
carry the §1 findings; `ScopeProbeModel` shows `ValidationContext.GetService` resolving per scope.
Re-run against a newer preview before reconsidering the pipeline.

## 7. Open

1. **Dev/admin pages stay English** — `TotpTestPage`, `AdminCopyChatToPlacePage` keep literal
   labels, so their messages read `Заполните поле «Chat ID».` Deliberate.
2. **`[Display(Name = …)]`** is load-bearing only as the *English* name the reverse index captures
   and the label then replaces; the 12 sites could be trimmed in a separate pass.
3. **`AliasValidationMessage.FieldLabel`** stays for its own non-validation hints.
4. **In-browser verification** — covered by unit tests only; the ru/en round-trip on a real form
   hasn't been done since the redesign.
