`tailwind.config.js` `content` only globs `src/dotnet/UI.Blazor*/**/*.{razor,cshtml}`
(plus `App.Server`). It does **not** scan `.ts` files.

Consequence: a CSS class referenced only from TypeScript (e.g. `classList.add('foo')`)
is treated as unused. Any rule inside `@layer { ... }` whose selector uses that class
gets **tree-shaken out of the built bundle.css** — silently, no build error.

**Why:** Tailwind tree-shakes `@layer` contents by detected class usage. Rules with
no class in the selector (e.g. `*:focus`) always survive; rules with an undetected
class do not.

**How to apply:** For CSS keyed off a JS-toggled class, either put the rule
**outside `@layer`** (plain CSS is emitted verbatim, never purged) or add the class
to the `safelist` in `tailwind.config.js`. The keyboard focus ring
(`body.keyboard-focus …` in `src/nodejs/styles/tailwind.css`) uses the
outside-`@layer` approach.
