# General project quirks

Each entry is a couple of sentences. The full write-up — evidence, commands, dates — is in
`../references/general/<name>.md`; open it when the one-liner turns out to matter.

## Performance traps

- **`element.getAnimations()` is document-wide.** It sorts every animation in the document, so
  calling it once per element is quadratic — batch by `effect.target` instead.
  → `../references/general/getanimations-is-document-wide.md`
- **`MarkupParser` could backtrack exponentially.** A 106-asterisk paste hung prod CPU for a
  week (Aug 26 – Sep 2 2026); memoization plus a step budget fixed it. Treat new markup-parsing
  regexes as adversarial input. → `../references/general/markup-parser-exponential-backtracking.md`

## Build-tool traps

- **Tailwind purges `@layer` rules whose class is only referenced from `.ts` files.** Make sure
  the class appears somewhere the content scanner reads, or the rule vanishes from the bundle.
  → `../references/general/tailwind-content-scan-ts.md`
- **Git LFS: "unable to unlink old `<file>`: Invalid argument"** during a rebase or checkout
  means a live process has the file memory-mapped. Move the file aside; do not start killing
  build processes. → `../references/general/git-lfs-unlink-invalid-argument.md`
