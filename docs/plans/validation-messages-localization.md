# Form Validation Messages Localization Plan

**Date:** 2026-08-05
**Branch:** `feat/3721-app-localization` (follow-up to `docs/plans/app-localization.md`
and `docs/plans/server-strings-localization.md`)
**Status:** Approved — design settled, all questions resolved in §8

## 0. TL;DR

Form validation messages are the one visible category of UI text that neither shipped
localization mechanism reaches. The JSON catalog (`AppStringLocalizer`) covers strings a
component renders from its own literals; `LocalizingUIActionFailureTracker` covers
server-thrown error text surfacing as toasts. Validation messages travel a third path —
DataAnnotations → `EditContext` → `ValidationMessageStore` → the form's validation
`<div>` — and nothing on that path touches a localizer.

**Design: resolve the rendered English message through three tiers, in order.**

| Tier | Mechanism | Cost | Covers |
|---|---|---|---|
| 1. Exact | English message → catalog key, dictionary hit | sync, free | app validator messages, inline `ErrorMessage`, per-field grammar overrides |
| 2. Template | English message reverse-matched against a `{0}`-template → key + args | sync, one regex | built-in `[Required]` / `[MinLength]` / `[EmailAddress]` output |
| 3. AI | `LocalizationUI.Get` → `ITranslations.GetTranslatedUIText` | async, cached | anything uncatalogued — a safety net, not the mechanism |

The critical property: **tiers 1 and 2 are synchronous**, so covered messages are correct
on first paint. That matters here more than anywhere else in the app — validation text
appears *while the user types*, and a translate-then-swap flicker mid-keystroke is
exactly the failure mode a pure-AI approach would produce. Tier 3 absorbs the tail, so
coverage is never zero and no message ever renders as a raw key.

What this buys over the alternatives (§6): **zero changes to validators, validation
attributes, form models, or `[Display]` names.** No key strings scattered across call
sites. The entire mechanism is one index plus a new `Messages.<lang>.json` catalog
(§4.2) — and the same index is what `docs/plans/server-strings-localization.md` §4.1
needs for the ~264 constant server error messages, several of which are parameterized
and therefore need exactly tier 2. One mechanism, two consumers.

Cost: ~16 new catalog keys × 14 languages, ~1 day of code.

## 1. Inventory — what is not localized today

### 1.1 App-owned validator messages (hardcoded English)

| Source | Message |
|---|---|
| `Core/Validation/Validators.Email.cs:18,21,25` | "Email address is invalid." (3 branches, 1 distinct) |
| `Core/Validation/Validators.Phone.cs:20` | "Phone number contains invalid characters." |
| `Core/Validation/Validators.Phone.cs:24` | "Phone number is too short." |
| `Core/Validation/Validators.Phone.cs:26` | "Phone number is too long." |
| `Core/Validation/PhoneOrEmailAsyncAttribute.cs:27` | "Enter a phone number or email address." |
| `UI.Blazor.App/Components/ChatSettings/AliasIdAttribute.cs:15` | "Custom link is too short." |
| `UI.Blazor.App/Components/ChatSettings/AliasIdAttribute.cs:17` | "Custom link should contain only 0-9, a-Z, - and _." |

All 7 are tier-1 exact matches. **None of this code changes** — we catalog its current
output rather than converting it to emit keys.

### 1.2 Inline `ErrorMessage =` literals

- `UI.Blazor.App/Components/DeleteAccountModal.razor:80` — "Delete confirmation is required"
- `UI.Blazor.App/Components/DeleteAccountModal.razor:81` — "Please enter DELETE to confirm"

Also tier 1, also unchanged. (`MLSearch.Service/Module/MLSearchSettings.cs:26` has one
too, but it is a server settings-binding message never shown to a user — out of scope.)

### 1.3 Adjacent hardcoded hints rendered in the same slot

- `UI.Blazor.App/Components/ChatSettings/AliasValidationMessage.razor:107,108` —
  "This link is available." / "This link is already in use."

Not validation results, but they render into the same `form-section-validation` element
and would look wrong left in English beside localized errors.

**These go through the ordinary `Strings.*.json` catalog, not the reverse index.** They
are static text the component owns, so `CODING_STYLE.md → Localization` applies as
written: a typed `AppStrings` member, compile-time-safe. The reverse index exists for
text the component *receives* and cannot key on; inverting a string we could simply have
keyed is strictly worse. `LocalizedMessage` therefore wraps only the validation branch of
`AliasValidationMessage` — passing an already-localized hint through it would reverse-look
up a translated string, miss, and hand it to tier 3 for a second translation.

### 1.4 Built-in DataAnnotations messages

16 usages of bare `[Required]`, `[MinLength]`, `[EmailAddress]`, `[RegularExpression]`
across `UI.Blazor` + `UI.Blazor.App` (against 8 usages of the app-owned attributes from
§1.1). Their text comes from .NET's own resources keyed on `CultureInfo.CurrentUICulture`,
which this app never sets — and under `InvariantGlobalization` satellite lookup would not
resolve anyway. So it is English, unconditionally.

Verified against .NET 10 (`ValidationAttribute.FormatErrorMessage`):

| Attribute | Exact output |
|---|---|
| `[Required]` | `The {0} field is required.` |
| `[MinLength(n)]` | `The field {0} must be a string or array type with a minimum length of '{1}'.` |
| `[EmailAddress]` | `The {0} field is not a valid e-mail address.` |
| `[RegularExpression(p)]` | `The field {0} must match the regular expression '{1}'.` |
| `[MaxLength]` / `[StringLength]` / `[Range]` | *(not used in any UI form today)* |

These are the tier-2 templates. Only the first three are needed now — the sole
`[RegularExpression]` usage (`DeleteAccountModal.razor:81`) overrides `ErrorMessage`, so
its framework text never renders. Add it anyway if you want the guard test to stay quiet
when someone uses it bare later; the plan leaves it out and lets the test say so.

### 1.5 `Display(Name = …)` values

12 sites, 6 distinct names — "Name", "Email", "Phone", "Short name", "Time zone",
"Phone or email". These supply the `{0}` that tier 2 extracts.

**They are a second, independent copy of the field's name, and a third of them have
already drifted from the label the user actually sees.** `FormSection`'s `Label`
parameter and the model's `[Display(Name = …)]` are unrelated strings, typically 150+
lines apart in the same file, with nothing tying them together:

| Component | `FormSection Label=` | `[Display(Name = …)]` | |
|---|---|---|---|
| `OwnAccountEditorModal.razor` | "Name" / "Phone" / "Email" | same | ✓ |
| `OwnAccountEditorModal.razor:42,248` | **"User link"** | **"Short name"** | ✗ |
| `ChatSettings/EditChatTypeModalPage:73,199` | **"Custom link"** | **"Short name"** | ✗ |
| `PlaceSettings/PlaceSettingsEditTypeModalPage:65,226` | **"Custom link"** | **"Short name"** | ✗ |
| `Settings/EmailSettings.razor:24,79` | **`@L.Email_Label`** (localized) | **"Email"** (literal) | ✗ |
| `Onboarding/{AvatarStep,PhoneStep,TimeZoneStep}` | "Name" / "Phone" / "Time zone" | same | ✓ |
| `SignIn/Modal/ProviderSelectStep.razor:314` | *(none — label is inside the input)* | "Phone or email" | — |

So a user editing their account sees a field labelled **User link** and, on error,
*"The Short name field is required."*

**Correction (2026-08-07, from in-browser testing).** This section originally claimed
`EmailSettings` renders a localized label above an English "Email" message. It didn't
render a message at all — and neither did the account editor's email field, which is the
only place `[AppEmailAddress]` reaches a user. Both passed `For="() => email"`, where
`email` is a Razor-local copy (`var email = _form.Email ?? ""`): `FieldIdentifier.Create`
keys on the closure object + `"email"` while the validator records against
`(_form, "Email")`, so `GetValidationMessages` returned nothing and the slot stayed empty.
`EmailSettings` compounded it — `IsValid(() => email)` was likewise always true, so
`EmailVerifier` showed for invalid addresses.

Fixed here (`For="() => _form.Email"`, `IsValid(() => _emailForm.Email)`) because the
first row of §1.1's inventory — `Validators.Email`, "Email address is invalid." — is
otherwise unreachable, and localizing text that never renders localizes nothing. A sweep
of all 40 `For="() => …"` bindings found these two and no others. Note `EmailSettings`
is currently not mounted anywhere (`51cdee9536 fix: settings modal` dropped its only
usage), so only the account editor's fix is observable.

This is why §4.4 takes `{0}` from the `FormSection`'s own label rather than maintaining a
parallel catalog of field names keyed on `[Display]`. `[Display]` stays as it is, but
stops being load-bearing.

**Totals: 9 message sentences + 3 templates + 1 labelless-field fallback = 13
`Messages.*` entries** (12 if `[RegularExpression]` is omitted per §1.4), plus 3 ordinary
`Strings.*` keys for the two hints above and the one below.

`ChatSettings/PlaceAliasRequiredValidationMessage.razor:4` renders a static "Custom place
link should be set first." into the same `form-section-validation` slot and carries a
`TODO: localize`. It is worth noting *how* it was missed — the inventory above was built
by tracing `GetValidationMessages()`, and that component never calls it; it just renders a
sentence into the validation slot. Sweeping for the CSS class instead is the reliable net.
That sweep (re-run at implementation time) returns exactly four sites: the two render
sites listed in §2, this component, and `AudioBlobDownloadTestPage`'s `@_status` (a dev
page, out of scope). Like the §1.3 hints, this one is component-owned static text, so it gets an
ordinary `Strings.*` key rather than an indexed one.

## 2. Why the existing machinery doesn't reach them

1. **`InvariantGlobalization=true`** (`Directory.Build.props:101`) — the standard
   `CultureInfo`-driven DataAnnotations story (`ErrorMessageResourceType` + satellite
   `.resx`) does not work here. Same constraint that forced `AppStringLocalizer`; see
   `docs/plans/app-localization.md` §1.
2. **Two validator implementations.** 24 components use Blazor's stock
   `<DataAnnotationsValidator/>`; 4 use our `<AsyncDataAnnotationsValidator/>` →
   `EditContextAsyncValidator`. We control only the second, so its
   `AppendValidationResults` (`Components/Validation/EditContextAsyncValidator.cs:129`)
   is **not** a usable choke point — it would cover 4 forms out of 28.
3. **The real choke point is render-time.** Exactly two components read validation
   messages for display:
   - `UI.Blazor/Components/Form/FormSection.razor:11` → rendered at `:49`
   - `UI.Blazor.App/Components/ChatSettings/AliasValidationMessage.razor:6` → rendered at `:21`

   (`UI.Blazor/Components/Form/Form.cs:97` also calls `GetValidationMessages()`, but
   only to test emptiness — unaffected.)

   Render-time is also the only point that sees *both* validator flavors, which is why
   the design hooks there rather than inside a validator.

## 3. Reuse (mandatory section)

### 3.1 Existing abstractions to reuse

- **`Strings.<lang>.json`** (`UI.Blazor/Resources/`, 14 languages, 373 keys) — the
  existing catalog, and the model for the new `Messages.<lang>.json` beside it (§4.2).
  Same format, same embedding mechanism, same test machinery — only the file name and
  the byte-match constraint (§4.5) differ.
- **`AppStringLocalizer`** (`UI.Blazor.App/Services/`) — forward key → text lookup.
  Registered scoped as both `IStringLocalizer<Strings>` and non-generic
  `IStringLocalizer` (`Module/BlazorUIAppModule.cs:35-36`). Its `this[name, args]`
  overload already does the `string.Format` tier 2 needs, and it already reports
  `ResourceNotFound` correctly.
- **`AppStrings`** (`UI.Blazor/Resources/AppStrings.cs`) — the
  `extension(IStringLocalizer l)` block of typed members. The new extension method
  follows this style but must live in its own static class (§3.2).
- **`LocalizationUI`** (`UI.Blazor.App/Services/LocalizationUI.cs`) — tier 3, unchanged.
  `[ComputeMethod] Get(string)` short-circuits to the input for English, otherwise
  `ConcurrentProcessor`-throttled AI translation via `ITranslations.GetTranslatedUIText`
  (`UITextKind.ErrorMessage`), cached by the Fusion compute cache.
- **`ComponentBase<THub>.L`** (`UI.Blazor/BaseTypes/ComponentBase.cs:44`, same member on
  `ComputedStateComponent`, `ComputedRenderStateComponent`, `FusionComponentBase`) — the
  localizer shortcut components already use. `AliasValidationMessage.razor` inherits
  `ComponentBase<AppUIHub>` and has `L`; `FormSection.razor` inherits Blazor's plain
  `ComponentBase` — but needs no localizer of its own under this design (§4.6).
- **`AppLocalizationTest`** (`tests/Chat.UI.Blazor.UnitTests/`) — already enforces
  key-set parity across languages, `{0}` preservation, and AppStrings-member ↔ key
  bijection. Its whole surface funnels through two helpers, `ShippedSubtags()` and
  `Load(subtag)`, parameterized only by the constants `Prefix = "Strings."` /
  `Suffix = ".json"` — so covering a second catalog file is a `prefix` parameter with a
  default, plus five `[Theory]` rows (§4.7). The bijection test needs no change at all.
- **`TestStringLocalizer`** (`tests/Chat.UI.Blazor.UnitTests/`) — for render tests.
- **`ITranslations.GetTranslatedUIText` + `UITextKind.ErrorMessage`** — the server-side
  translate-and-cache endpoint behind tier 3. Already shipped, already cached.

No fit exists for: a reverse (English → key) index with template matching. That is the
one genuinely new thing here.

### 3.2 Reusability of new components

- **`Messages.<lang>.json`** (`UI.Blazor/Resources/`) — the new message catalog, 14
  files, embedded exactly like `Strings.*.json` (one more `<EmbeddedResource>` line in
  `UI.Blazor.csproj` next to the existing glob at `:20`, same
  `LogicalName="%(Filename)%(Extension)" WithCulture="false"`).
- **`MessageIndex`** — builds and holds the exact / template / field-name indexes.
  **Recommended placement: `src/dotnet/UI.Blazor/Resources/`.** It must sit in
  `UI.Blazor` because both render sites need it and one of them (`FormSection`) is in
  that project — and that is also where `Messages.*.json` is embedded, so the index
  reads its own assembly's resource with no new plumbing and no DI.
  It is deliberately **not** validation-specific: `server-strings-localization.md` §4.1
  is its second consumer.
- **`MessageLocalizer`** — the `extension(IStringLocalizer l)` block exposing
  `TryMessage(string)`. Same folder. It must **not** go inside `AppStrings`:
  `AppLocalizationTest.AppStringsMembersMatchEnglishKeysExactly` requires every
  `AppStrings` member to correspond to a catalog key, and a helper method fails it.
- **`IUITextLocalizer`** — one-method abstraction over tier 3, needed because
  `FormSection` is in `UI.Blazor` while `LocalizationUI` is in `UI.Blazor.App` and the
  dependency runs App → Blazor. **Recommended placement:
  `src/dotnet/UI.Blazor/Services/`**, implemented by `LocalizationUI`. Its `Get` already
  has the exact signature, so this is a one-line change. A local interface in
  `UI.Blazor.App` would not solve the layering problem, which is the entire point.
- **`LocalizedMessage`** component — sync-first render with async fallback.
  **Recommended placement: `src/dotnet/UI.Blazor/Components/`** (shared), not inside
  `Form/`. It is the natural primitive for any "English text of unknown provenance on
  screen" case; `ToastHost.razor` is in the same project and is the obvious second
  consumer.

## 4. Design

### 4.1 Resolution order

```
Localize(english):
  1. ExactIndex[english]        -> key            -> l[key]                    (sync)
  2. TemplateIndex.Match(english) -> key + args    -> l[key, LocalizeArgs(args)] (sync)
  3. IUITextLocalizer.Get(english)                                             (async)
     -> render `english` verbatim until it resolves; if it never does, stay English
```

Exact beats template so a per-field override can win over the generic template — the
mechanism for fixing grammar in inflected languages (§4.5).

### 4.2 Building the index

**Indexed entries live in their own catalog: `Messages.<lang>.json`, beside
`Strings.<lang>.json`.** Everything in `Messages.*` is reverse-indexed; nothing in
`Strings.*` ever is.

Membership is structural, not a naming convention, which matters for three reasons:

1. **It cannot be got wrong.** The alternative — reverse-indexing entries of
   `Strings.*.json` whose key starts with a registered prefix — makes "is this indexed?"
   depend on a list in `MessageIndex` that §7 says will grow at least twice. Forget to
   register a prefix and those entries silently never index, falling through to AI
   translation: the §4.9 failure mode, invisible.
2. **It keeps ordinary catalog entries out.** Labels, buttons and titles must never be
   inverted — their values collide (`Email_Title` and `Email_Label` are both "Email"
   today), which would make lookups ambiguous.
3. **It makes §4.5's unusual constraint discoverable.** Entries here must byte-match
   runtime output. Nobody editing a general-purpose `Strings.en.json` would suspect
   that; a separate file with a header comment states it.

It also means `AppLocalizationTest`'s `AppStringsMembersMatchEnglishKeysExactly` — which
demands one typed `AppStrings` member per catalog key — simply never sees these keys. No
exemption carve-out, no dead members.

Inside `Messages.*`, prefixes still route between the two indexes. That is a real
semantic distinction, not a bookkeeping flag:

- **Message index** (`Validation_`, `Error_`) — entries whose English value contains no
  `{n}` go to `ExactIndex`; entries with `{n}` are compiled into `TemplateIndex`.
- **Field index** (`Field_`) — English name → key, used only as the labelless fallback
  for arg substitution (§4.4). One entry today.

Keeping the two separate prevents a one-word field name from shadowing a message and
makes the uniqueness rule enforceable per index (§5).

`AppStringLocalizer.LoadAll` loads both files per language and merges them, so forward
key → text lookup is unchanged for callers; a test asserts the two never collide on a key.

### 4.3 Template matching

For each templated English value, compile a regex once at index build:

1. Split on `{n}` placeholders.
2. `Regex.Escape` each literal segment.
3. Join with non-greedy capture groups, anchored `^…$`.
4. Reject at build time any template with **adjacent placeholders** (`{0}{1}`) — those
   are genuinely ambiguous. None exist; the check keeps it that way.

Match order: `ExactIndex` (a dictionary hit) first, then templates sorted by descending
total literal length, so the most specific template wins.

The three real templates are all unambiguous — every placeholder is separated by literal
text, and `[MinLength]`'s second arg is quoted:

```
The {0} field is required.
The field {0} must be a string or array type with a minimum length of '{1}'.
The {0} field is not a valid e-mail address.
```

### 4.4 Argument handling — the field name comes from the label

The field-name arg is **not** looked up in a catalog. It is replaced by the hosting
`FormSection`'s own `Label`, which is the string the user is looking at three lines above
the message, and which §9.1 will already have localized.

```
FormSection(Label: "User link")
  message:  "The Short name field is required."
  template: "The {0} field is required."   arg0 = "Short name"  ← discarded
  render:   "The User link field is required."   (localized label once §9.1 lands)
```

Why this rather than a `Field_*` catalog keyed on `[Display]`:

- **It cannot drift.** §1.5 shows four of eleven sites already disagree with their label,
  including one where the label is localized and the message is not. Substituting the
  label makes the two consistent by construction and fixes those four as a side effect.
- **It dissolves the collision problem.** Three fields share `[Display(Name = "Short
  name")]` while their labels read "User link" / "Custom link" / "Custom link". A catalog
  keyed on the display name gives all three one translation matching none of them; keyed
  on the label, each gets its own — with no per-site annotation, because the label is
  already per-site.
- **It removes 6 keys and 12 `[Display]` values from the localization surface.**
  `[Display]` stays for the framework's benefit but is no longer load-bearing, and could
  be deleted later.

Non-field args keep the pass-through rule, which remains correct without configuration:

- `[MinLength]`'s `{1}` = "1" → a number, substituted verbatim.
- `[RegularExpression]`'s `{1}` = "^DELETE$" → verbatim. Important: a translated regex
  would be a bug.

**Which arg is the field name.** All three templates in use put it at `{0}` (§1.4), so
the rule is *"arg 0 of a `Validation_` template is the field name"*. The prefix condition
is not decoration: §7's server messages are templated too
(`"You can send up to {0} messages…"`) and their arg 0 is a count, not a field — an
unconditional convention would substitute a form label into it. The index therefore
decides per entry at build time, from the same prefix routing §4.2 already performs, and
carries the answer on the compiled template. A fourth `Validation_` template that puts the
field elsewhere replaces that one-line rule with an explicit arg index; the escape is
already in the right place.

**Fallback when there is no label.** `ProviderSelectStep.razor` renders its label inside
the input and passes no `Label` (§1.5). For sites like it, fall back to a small
`Field_*` lookup on the extracted English arg — one entry today
(`Field_PhoneOrEmail`) — and verbatim English if that misses too. Degraded, not broken.

### 4.5 Catalog shape

For an indexed entry, the **English value must byte-match the text actually produced at
runtime**, because it serves double duty as the reverse-lookup source and as what
English users see. That sounds fragile; §5 makes it self-checking.

```jsonc
// Messages.en.json — values here must byte-match what the app produces at runtime
"Validation_Required_Format":  "The {0} field is required.",
"Validation_EmailInvalid":     "Email address is invalid.",
"Validation_PhoneRequired":    "The Phone field is required.",   // exact override
"Field_PhoneOrEmail":          "Phone or email",                // labelless fallback (§4.4)
```

```jsonc
// Messages.ru.json — same keys, natural translations
"Validation_Required_Format":  "Заполните поле «{0}».",
"Validation_EmailInvalid":     "Некорректный адрес электронной почты.",
"Validation_PhoneRequired":    "Укажите номер телефона.",        // beats the template
"Field_PhoneOrEmail":          "Телефон или эл. почта",
```

`Validation_PhoneRequired` shows the escape hatch for the `{0}`-composition grammar
problem: templates are fine for English and most languages, but interpolating a noun
into a fixed frame reads poorly in inflected ones. Add exact overrides only where a
translator flags them — start with none.

### 4.6 Rendering

`LocalizedMessage.razor` (`UI.Blazor/Components/`):

- On render, try tiers 1–2. On a hit, render the result directly — a plain
  `ComponentBase`, no Fusion state, no allocation beyond the lookup. This is the hot
  path (it runs per keystroke) and must stay cheap.
- On a miss, render the English text and kick off `IUITextLocalizer.Get`, then
  `StateHasChanged()` when it completes.
- **Resolve both `IStringLocalizer` and `IUITextLocalizer` with `GetService`, not
  `[Inject]` / `GetRequiredService`**, falling back to rendering `Text` verbatim. Both are
  registered in `BlazorUIAppModule` — one layer up in `UI.Blazor.App` — so a hard
  dependency would make this component throw at render time wherever the App layer is
  absent. That never happens in production today (`UI.Blazor` is consumed only by
  `UI.Blazor.App` and by `tests/UI.Blazor.UnitTests`, which renders nothing), but it is
  about to: §5's bUnit tests render forms directly. English is the only sensible output
  without the App layer anyway, since language selection lives in `LanguageUI` there.
- It follows that **`FormSection` needs no localizer of its own** — it passes two strings
  it already holds and does no lookup. Same for `AliasValidationMessage`.
- Tier 3 does not auto-invalidate on language change (a plain component holds no Fusion
  dependency). Acceptable: switching UI language reloads the picker's host anyway — see
  `docs/plans/app-localization.md`'s amendment — and tier 3 is the rare path.
- A `FieldLabel` parameter carries the hosting section's `Label` for §4.4's substitution;
  empty means "no label", which selects the `Field_*` fallback.

Call sites:

- `FormSection.razor:49` → `<LocalizedMessage Text="@messages.First()" FieldLabel="@Label"/>`.
  `Label` is already a parameter of `FormSection`, so this is local — no plumbing.
- `AliasValidationMessage.razor:21` → same, for the validation branch only (the two hints
  at `:107-108` are ordinary catalog strings — §1.3). It renders as a `ValidationMessage`
  fragment *inside* a `FormSection`, so it cannot read `Label` itself and needs a
  `FieldLabel` parameter.

  **`FormSection.ValidationMessage` becomes a `RenderFragment<string>` carrying the
  section's `Label`**, so the three call sites write `<ValidationMessage
  Context="fieldLabel">` … `FieldLabel="@fieldLabel"`. Passing the literal instead
  (`FieldLabel="Custom link"` four lines under `Label="Custom link"`) would recreate at
  four lines' distance exactly the drift §4.4 exists to eliminate — and the screen pass
  that localizes `Label` is precisely the moment someone would update one and not the
  other.

  The change is cheap: only four sites use the fragment (`OwnAccountEditorModal:44`,
  `EditChatTypeModalPage:75`, `PlaceSettingsEditTypeModalPage:67`, and
  `AudioBlobDownloadTestPage:18`). The fourth ignores the label but still needs an explicit
  `Context=`: it nests inside `Form`'s own child content, and Razor rejects two enclosing
  fragments both binding the implicit `context` (RZ9999). Worth knowing before assuming
  "content that ignores the context compiles unchanged" — it does only where no outer
  typed fragment is in scope.

  Cascading from `FormSection` was considered and rejected: `FormSection` currently has
  no `CascadingValue` (it only *consumes* the `EditContext` cascade), so this would add a
  component layer to the render path of all 28 forms. `RenderFragment<string>` gets the
  same structural coupling with no extra component and no cascade.

### 4.7 Extending `AppLocalizationTest` to a second catalog

The existing suite is already shaped for this: every test reaches the resources through
`ShippedSubtags()` and `Load(subtag)`, both parameterized only by the constants
`Prefix = "Strings."` and `Suffix = ".json"`. Give each a `string prefix = Prefix`
parameter and all current tests compile untouched, still covering `Strings.*`.

Five catalog-shape tests then become `[Theory]`s over both prefixes:

- `EnglishFallbackIsComplete`
- `EveryShippedTranslationMapsToKnownLanguage`
- `EveryShippedTranslationMatchesEnglishKeys` — the `{0}`-preservation check, which
  matters more for templates than for anything in the catalog today
- `EveryShippedTranslationShouldTranslateEveryEnglishKey`
- `EverySupportedUILanguageShouldShipTranslation`

Three stay `Strings.*`-only: `EveryAppStringsMemberReadsItsOwnKey`,
`EveryAppStringsMemberResolvesInEveryLanguage`, and `EverySentenceFragmentPairHasContent`
(the `_Prefix`/`_Suffix` convention doesn't apply to messages).

`AppStringsMembersMatchEnglishKeysExactly` needs **no** change — it scans `Strings.*`,
which no longer contains any indexed key. This is the structural payoff of §4.2: the
bijection invariant keeps its full strength instead of acquiring a carve-out.

### 4.8 Steps

1. `Messages.en.json` (empty object) + the `<EmbeddedResource>` line in
   `UI.Blazor.csproj`; teach `AppStringLocalizer.LoadAll` to load and merge both files.
2. `MessageIndex` + `MessageLocalizer` in `UI.Blazor/Resources/`, with the index-build
   validations (adjacent placeholders, per-index uniqueness).
3. `LocalizedMessage.razor` with its `FieldLabel` parameter; `FormSection.ValidationMessage`
   → `RenderFragment<string>`; wire both render sites and the three
   `AliasValidationMessage` call sites (§4.6). Nothing changes yet — the index is empty,
   everything falls through to English.
4. `IUITextLocalizer` in `UI.Blazor/Services/`; implement on `LocalizationUI`; register
   in `BlazorUIAppModule`.
5. Parameterize `AppLocalizationTest` over the two catalogs (§4.7).
6. Add the 13 keys to `Messages.en.json`, English values matching runtime output exactly,
   and the 3 ordinary `Strings.*` keys of §1.3 with their `AppStrings` members.
7. Write the guard test (§5). It should pass at this point — that is what proves step 6
   got the English right.
8. Translate into the other 13 languages (13 × 16 = 208 strings).
9. Fallback telemetry (§5) + render tests.

Steps 1–5 are behavior-neutral and can land independently of the translation work.

Note that step 3 changes user-visible English text at four sites, before any translation
lands: the messages there start naming the field by its label ("User link", "Custom
link", localized `Email_Label`) instead of by its `[Display]` name ("Short name",
"Email") — see §1.5. That is the intended fix, not a regression, but it is the one point
in the sequence where English output changes, so it wants its own commit.

### 4.9 Risks

- **The fallback masks gaps.** If a template stops matching — a .NET upgrade rewording a
  resource string, or someone editing `Validators.Phone.cs` — the app silently degrades
  to AI translation instead of failing. Nobody notices. **This makes the guard test
  mandatory, not optional**; it is the entire safety story. Pair it with the
  fallback counter so production also tells you.
- **Tier 3's quality risks still apply to uncatalogued text** — an LLM may mangle the
  alias charset sentence or translate the literal `DELETE` token, making that form
  unsubmittable. Mitigated by the fact that both of those messages are catalogued at
  tier 1 from day one. The rule to keep: anything with a literal token or a technical
  constraint gets an exact entry, never the fallback.
- **Tier 3 does not substitute the label.** It hands the raw English message to the
  translator, so an uncatalogued templated message comes back naming the field by its
  `[Display]` value ("Short name") rather than by its label ("User link"). The fallback
  is therefore not merely lower-quality than tiers 1–2, it is *inconsistent* with them —
  which is another reason the guard test is what keeps the three templates catalogued.
- **English-value drift.** §4.5's byte-match requirement is unusual and will surprise
  someone. The separate `Messages.*` file is the main mitigation (§4.2); back it with a
  header comment naming the constraint and pointing at the guard test.
- **Two catalogs to keep in sync per language.** 14 files become 28, and a translator now
  has two files per language rather than one. Arguably a feature — the message catalog
  has a different review standard — but it is real overhead.
- **Index build cost** is a one-time static parse of a small JSON — negligible, and
  unlike the single-file variant it does not re-parse the 373-key catalog.
- **The "arg 0 of a `Validation_` template is the field name" rule** (§4.4) is not
  enforced by the type system. A future template that puts the field elsewhere would
  silently substitute the label into the wrong slot. Pinned by a test; the escape is an
  explicit arg index on the compiled template.
- **Label quality now shows up in error messages.** Substituting the label means a vague
  or over-long label degrades the message too ("The Custom link field is required."). Not
  new — it is the same string the user already sees — but it does couple two things that
  were independent, so a bad label is now visible twice.

## 5. Testing

**The guard test (the one that matters).** Table-driven, behavioral: drive the *real*
attributes and validators with invalid input, and assert every produced message resolves
through tier 1 or tier 2 — never tier 3.

- One row per built-in attribute in use (`[Required]`, `[MinLength(1)]`,
  `[EmailAddress]`) × a representative `[Display]` name, asserting the framework's
  actual `FormatErrorMessage` output matches a template.
- One row per app-validator branch: `Validators.Email.Validate` (3 branches),
  `Validators.Phone.Validate` (3), `PhoneOrEmailAsyncAttribute`, `AliasIdAttribute` (2),
  `DeleteAccountModal`'s two `ErrorMessage` literals.

This is what makes the English byte-match requirement safe. A .NET upgrade that rewords
`RequiredAttribute_ValidationError`, or a developer tweaking a validator's wording,
turns into a red build instead of an invisible degradation.

**Index invariants.**

- No two indexed keys share an English value, per index.
- No template has adjacent placeholders.
- `Messages.*.json` and `Strings.*.json` never define the same key (they are merged for
  forward lookup — §4.2).
- Every key in `Messages.*` starts with a known routing prefix (`Validation_`, `Error_`,
  `Field_`), so a typo'd prefix can't produce an entry that is loaded but never indexed.
- Every `{n}` in an indexed English value survives into all 13 translations (already
  covered by `EveryShippedTranslationMatchesEnglishKeys` once parameterized — §4.7).
- Every template in use puts the field name at `{0}` — the §4.4 convention, pinned
  explicitly so a fourth template can't break it silently.

**Unit.** Template compilation and matching: single placeholder, two placeholders with
literal separator, no match, exact-beats-template ordering. Label substitution: label
supplied → replaces arg 0; label empty → `Field_*` fallback; neither → verbatim English;
non-field args (`'1'`, `'^DELETE$'`) pass through untouched in every case.

**Render (bUnit).** For one form per validator flavor (stock `DataAnnotationsValidator`
and `AsyncDataAnnotationsValidator`), submit invalid input and assert the rendered
`form-section-validation` text equals the expected localized string under a non-English
`TestStringLocalizer`. Include one section whose `Label` differs from its `[Display]`
name (`OwnAccountEditorModal`'s alias field) — that case is the whole point of §4.4 and
would otherwise go untested.

**Telemetry.** Count tier-3 fallbacks — the production signal for "what should be
catalogued next" and the backstop for a guard test that missed a path. Note the counter
must **not** be tagged with the message: uncatalogued text is unbounded by definition, and
that is precisely the wrong thing to make a metric dimension. Ship an untagged counter
(`app.ui.localization.fallback.count`) for the rate, and log each *distinct* message once
at Warning for the identity — logs tolerate high cardinality, metrics don't.

## 6. Rejected alternatives

**A — static keys emitted by validators.** Validators return catalog keys
(`"Validation_PhoneTooShort"`); built-in attributes get app-owned wrappers
(`AppRequiredAttribute` overriding `FormatErrorMessage`) so `[Display]` values flow
through as encoded args (`"<key>|<arg>"`). Exact and offline, but it rewrites 24
attribute usages, 12 `[Display]` names, 4 attribute types and 2 validators, and
introduces a string encoding with a separator-collision risk. The three-tier design gets
the same exactness for the same catalog cost with none of the call-site churn.

**A′ — `ErrorMessage` key per call site.** `[Required(ErrorMessage =
"Validation_NameRequired")]`. Drops the wrappers and the arg encoding entirely, and
per-field sentences translate better than `{0}` templates. Rejected because it scatters
two dozen unchecked string literals across form models — the objection that motivated
this redesign. Note the three-tier design keeps A′'s one real advantage: §4.5's
exact-override tier gives per-field sentences where they read better, without putting
keys in attributes.

**B — pure runtime AI translation.** `LocalizedText` on every message, straight to
`LocalizationUI`. Zero catalog work, but English renders first and swaps 1–3 s later —
mid-keystroke, on the one screen where the user is actively typing. Also
non-deterministic, network-dependent, and unreviewable; translating the `DELETE`
confirmation token would break the form outright. Survives as tier 3, where its
unbounded coverage is an asset and its latency is not.

## 7. Relationship to the server-strings plan

`docs/plans/server-strings-localization.md` §4.1 proposes a client-side
`ServerMessageLocalizer`: an exact-match table over ~264 constant `StandardError`
messages, with AI fallback for the parameterized and unknown ones. That is tiers 1 and 3
of this design, missing tier 2 — which is precisely what its parameterized cases need
(`$"You can send up to {limit} messages until this user adds you to their contacts or
replies."`).

`MessageIndex` should therefore be built as a general message-localization index from
the start — `Messages.*.json` is deliberately named for messages in general, not for
validation, and `Error_` is reserved in §4.2 alongside `Validation_`. When that plan
lands, its ~264 constant messages become `Error_*` entries in the same file and
`ServerMessageLocalizer` folds into `MessageIndex` rather than being written separately.
That plan's §4.1 should be updated to point here once this lands.

## 8. Resolved

**Q1 — how does an entry opt in to being reverse-indexed?** *(2026-08-05)* Decided:
a separate `Messages.<lang>.json` catalog, structural membership (§4.2). The competing
option — a registry of indexed key prefixes inside `Strings.*.json` — was rejected once
the test cost of a second catalog turned out to be roughly an hour (§4.7) rather than a
rewrite of the parity machinery. Deciding factor: the separate file also dissolves the
`AppStrings` bijection carve-out entirely, so an existing invariant keeps full strength
instead of gaining an exemption.

**Q2 — exempt indexed keys from the AppStrings bijection test, or add dead members?**
Dissolved by Q1: keys in `Messages.*` are invisible to a test that scans `Strings.*`.

**Q3 — how is the field name in `{0}` identified?** *(2026-08-06)* Decided: take it from
the hosting `FormSection`'s `Label`, not from a `Field_*` catalog keyed on
`[Display(Name = …)]` (§4.4).

The question started as "two fields sharing a display name can't diverge in translation",
with a proposed escape hatch of cataloguing the whole sentence at tier 1. **That escape
hatch does not work** — tier 1 keys on the full English message, and two fields with the
same display name produce byte-identical messages, so tier 1 cannot tell them apart
either. Both tiers are blind to the distinction for the same reason.

Investigating that turned up the real problem: `Label` and `[Display]` are independent
strings, and four of eleven sites had already drifted (§1.5), including `EmailSettings`,
which renders a *localized* label above a message that names the field in English.
Sourcing `{0}` from the label fixes the drift, dissolves the collision question (labels
are per-site, so no two fields share one), removes 6 keys and 12 `[Display]` values from
the localization surface, and needs no per-site annotation.

Residual: the "arg 0 is the field name" convention (§4.4) and the labelless fallback for
`ProviderSelectStep`. Both are pinned by tests.

**Q4 — `FormSection` base class: inject `IStringLocalizer`, or reparent to
`ComponentBase<UIHub>`?** *(2026-08-06)* Dissolved by Q3. The question assumed
`FormSection` would localize the message itself (`L.Validation(messages.First())`). Under
§4.6 it renders `<LocalizedMessage>` and passes two strings it already holds, doing no
lookup — so it needs no localizer and no reparent. The residual concern moved to
`LocalizedMessage`, which resolves its services optionally rather than by `[Inject]`
(§4.6).

**Q5 — drop the redundant `[MinLength(1)]`?** *(2026-08-06)* **Done.** Removed from all
8 sites (not 7 — `AdminCopyChatToPlacePage` has two). Verified empirically rather than
from the docs, and the redundancy is stronger than assumed: `[Required]` rejects a strict
*superset* of what `MinLength(1)` rejects on a string.

| value | `[Required]` | `[MinLength(1)]` |
|---|---|---|
| `null` | reject | **accept** |
| `""` | reject | reject |
| `" "` | reject | **accept** |
| `"x"` | accept | accept |

So `MinLength(1)` never rejected anything `[Required]` didn't already, on any of the 8
sites — all of which are `string` and all of which carry `[Required]`. `UI.Blazor.App`
builds clean after the change. This removes the `[MinLength]` template from §1.4's tier-2
set; it is retained in the catalog anyway, since a future bare `[MinLength(n)]` with
`n > 1` is meaningful and would otherwise fall through to AI.

Follow-up, not done: `AdminCopyChatToPlacePage.razor:72` carries
`[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:…", Justification = "String.Count
should be preserved.")]`, which exists because `MinLengthAttribute` reflects on `Count`.
With `MinLength` gone it is probably dead, but it is AOT-trimming machinery and this
repo does NativeAOT builds — verify against an AOT build before removing.

**Q6 — should this plan absorb the form-label localization pass it now depends on?**
*(2026-08-06)* Decided: **no — stay narrow.** §4.4 renders the label into the message, so
until a screen's labels are localized its messages carry an English noun in a translated
sentence. That is acceptable, for three reasons in ascending weight:

1. **The output is self-consistent by construction.** The message names the field with the
   exact string rendered above (or, under `IsLabelInsideInput`, inside — `FormSection.razor:35`
   renders the label either way) the input. English label → English noun the user is
   looking at. There is no state in which message and screen disagree. The rejected
   `Field_*` catalog would have produced the opposite: a *translated* field name above an
   *English* label, which reads as a bug rather than as an untranslated screen. The
   coupling §4.4 introduces is what makes the interim state benign.
2. **A label-only pass produces a worse artifact than doing nothing.** Of the 27
   components hosting a `FormSection`, exactly 4 use the localizer at all —
   `TranscriptionSettings`, `ApiKeyCreateFormPage`, `EmailSettings`, `LanguageSettings`,
   all Settings-modal tabs, i.e. `app-localization.md` §2's MVP screen. The other 23 have
   **zero** `L.` usages; they are untouched, not half-done. Translating one attribute in
   each yields a modal whose title is "Edit account" and whose button is "Save", with one
   translated noun inside one error sentence — and Phase 2 reopens every one of those
   files anyway, so the review cost is paid twice.
3. **Where it matters today it is already correct.** Both validated forms on the one
   localized screen (`EmailSettings` → `Email`, `ApiKeyCreateFormPage` → `Name` /
   `ExpiresInDaysText`) already pass `Label="@L.*"`, so their messages are fully localized
   on day one with no label work at all.

The rule this leaves behind, and the only place the two efforts touch: **a screen's Phase-2
localization pass localizes its `FormSection` `Label`s, and its validation messages follow
with no extra work.**

*Confirmed on the first such pass (2026-08-07, `OwnAccountEditorModal`).* Localizing that
screen needed 5 new keys — the four field labels already existed as `YourAccount_*`, and
`Common_Save`/`Common_Cancel` were reused — plus `Common_Optional` in the shared `Label`
component and `Common_Verified`/`Common_Unverified` in `VerificationStatus`. Its
validation messages changed from `Заполните поле «Name».` to `Заполните поле «Имя».` with
no edit to any validator, attribute, catalog entry or form model: the label is the only
input. That is the entire mechanism paying off, and the reason to keep the two efforts
separate.

## 9. Open questions

None.
