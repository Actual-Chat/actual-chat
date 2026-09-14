To tie a Play Console version back to source:

- **Android versionCode = 65536 × minorIndex + nbgv height.** 2.15 → 527, 2.16 → 528, 2.17 → 529, 2.18 → 530 (so 2.17.246 = 65536×529 + 246 = 34668790). The patch component *is* the nbgv git height, which is what makes the rest work.
- **Find the commit:** `nbgv get-version <commit> -v SimpleVersion --public-release=true` (nbgv is on PATH). Walk `git log --first-parent origin/release/v2.N` and match the height. `nbgv get-version -v` is `--variable`, not a commit selector — the commit-ish is positional.
- **Then check inclusion:** `git merge-base --is-ancestor <fix> <shippedCommit>`. `git branch -r --contains` only tells you the branch, and a release branch keeps gaining commits after its store build, so branch membership ≠ shipped.
- Release branches are cut mid-cycle: the `Set version to '2.N'` commit sits ~25 commits below the branch tip, and the shipped build is often *not* the tip (2.17.246 was 7 below tip 2.17.253; 2.19.147 was 1 below 2.19.148).

Release dates come from the Play Console App version filter panel — see [[play-console-anr-browsing]].
