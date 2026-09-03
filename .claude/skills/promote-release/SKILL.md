---
name: promote-release
description: |
  Publish a tested Voxt release to the stores: Google Play production, App
  Store review (iOS + Mac Catalyst), Microsoft Store certification. Use when
  the user says "promote the release", "publish the apps", "release to the
  stores", "/promote-release", or after /prepare-release once the staged
  builds have been tested.
allowed-tools: Bash, Read, Grep, Glob, AskUserQuestion
---

# promote-release

Second half of a release. `/prepare-release` cut `release/vX.Y`; its CI run
(after the `prod` environment approval) **staged** the apps — Play internal
track, TestFlight for iOS and Mac, a pending Microsoft Store submission — but
published nothing. This skill confirms the builds were tested, then dispatches
`promote-release.yml` **on the release branch**, which publishes them with the
store notes from `docs/releases/store-notes-vX.Y.txt`.

**This publishes to end users.** The one hard stop is step 3: never dispatch a
platform the user hasn't confirmed as tested. The store jobs run in the
`prod-store` GitHub environment, which admits `release/*` branches only; it
has no reviewer gate, so the user's answer in step 3 is the only gate.

## Reuse

- **`promote-release.yml`** does all store work; the skill only gathers inputs
  and dispatches it with `gh workflow run`. No store API calls from here.
- **`gh run list` / `gh api .../artifacts`** identify the release run and the
  build version — the same data the CI run already produced.
- **Store notes** come from `/prepare-release` step 5b. Don't rewrite them
  here; if the file is missing, follow that step's rules and commit it to
  the release branch.

## Steps

### 1. Identify the release and its build version

The release branch is `release/vX.Y` — the highest `origin/release/v*` unless
the user names one. Find its latest CI run and read the build version from
the artifact names (`chat.actual.app.<X.Y.Z>.ipa`):

```bash
git fetch origin
branch=$(git branch -r --list 'origin/release/v*' | sed 's|.*origin/||' | sort -V | tail -1)
run=$(gh run list --repo Actual-Chat/actual-chat --workflow build-test-deploy-dev.yml \
        --branch "$branch" --json databaseId,conclusion,createdAt,url --limit 5)
echo "$run" | jq -r '.[] | "\(.databaseId)\t\(.conclusion)\t\(.createdAt)\t\(.url)"'
```

Pick the newest run whose staging jobs succeeded and get the version:

```bash
gh run view <id> --repo Actual-Chat/actual-chat --json jobs \
  -q '.jobs[] | select(.name | test("Deploy .* (play store|apple app store)|Upload win")) | "\(.conclusion)\t\(.name)"'
gh api repos/Actual-Chat/actual-chat/actions/runs/<id>/artifacts -q '.artifacts[].name' | grep -o '[0-9]\+\.[0-9]\+\.[0-9]\+' | sort -u
```

The version is `X.Y.Z` (nbgv SimpleVersion, e.g. `2.17.246`). A staging job
that failed or was skipped means that platform has nothing to promote — leave
it out in step 3 and say why.

**The Windows pending submission is whatever the latest release-branch run
uploaded**: each upload replaces the previous draft. If a newer run than the
one you picked has a green Windows upload job, that's the package Partner
Center holds — promote Windows only if the versions match.

### 2. Check the release branch carries the workflow and the store notes

```bash
git show origin/release/vX.Y:.github/workflows/promote-release.yml > /dev/null   # must exist
git show origin/release/vX.Y:docs/releases/store-notes-vX.Y.txt | wc -m         # must exist, ≤ 500
```

The workflow runs on the release branch and reads both from there. If the
store notes are missing, write them per `/prepare-release` step 5b (plain
text, ≤ 500 chars), show them to the user, and commit to `release/vX.Y` with
`docs: add store notes vX.Y`, then merge that into `dev` as in
`/prepare-release` step 7. A release branch cut before the workflow existed
needs the workflow cherry-picked onto it first — say so and stop.

### 3. Confirm the builds were tested — HARD STOP

Ask with `AskUserQuestion` (multiSelect), listing exactly what was staged, e.g.:

> Which staged builds of X.Y.Z did you test? Only the selected platforms are
> promoted.
> - Android — Play internal track, version code N
> - iOS — TestFlight X.Y.Z
> - macOS — TestFlight X.Y.Z (Mac Catalyst)
> - Windows — MSIX artifact / pending Store submission X.Y.Z.0

Then ask, in the same call, the App Store version to publish under: the
convention is `X.Y` for the first release from the branch and `X.Y.1`,
`X.Y.2`, … for follow-ups. "Auto" (empty input) lets the workflow pick the
next free one from App Store Connect — recommend it. Offer a Play rollout
percentage only if the user brings it up; default is a full release.

No platform selected → stop, nothing to do. Never infer "tested" from a green
CI run, from the user having approved the `prod` environment, or from a prior
conversation.

### 4. Dispatch the promotion

```bash
gh workflow run promote-release.yml --repo Actual-Chat/actual-chat --ref release/vX.Y \
  -f version=X.Y.Z \
  -f android=<true|false> -f ios=<true|false> -f macos=<true|false> -f windows=<true|false> \
  -f apple-version="" -f android-rollout=100
sleep 5
gh run list --repo Actual-Chat/actual-chat --workflow promote-release.yml --limit 1 --json databaseId,url
```

Tell the user the run URL, then watch:

```bash
gh run watch <id> --repo Actual-Chat/actual-chat --exit-status
```

Expect it to take a while: the iOS/macOS jobs wait for App Store Connect to
finish processing if needed, and the Windows job polls Partner Center until
pre-processing accepts the package (minutes). If `gh run watch` is
interrupted, resume it; don't re-dispatch.

### 5. Report

Read the run's job summaries (`gh run view <id> --log` for failures) and
report per platform:

- Android: released to production (rollout %), version code.
- iOS / macOS: App Store version string and build submitted for review.
- Windows: submission id, in certification (hours; watch in Partner Center).

For a failed job, quote the error and stop — don't retry blindly. The usual
causes: build not on the internal track (wrong version), App Store version
already waiting for review (cancel it in App Store Connect), no pending
Microsoft Store submission (the release run's Windows upload didn't run).

## Quick reference

| Step | Command / action |
|---|---|
| Release run | `gh run list --workflow build-test-deploy-dev.yml --branch release/vX.Y` |
| Build version | artifact names `chat.actual.app.<X.Y.Z>.ipa` → `X.Y.Z` |
| Store notes | `docs/releases/store-notes-vX.Y.txt` on `release/vX.Y`, ≤ 500 chars |
| Tested? | `AskUserQuestion`, multiSelect per platform — hard stop |
| Dispatch | `gh workflow run promote-release.yml --ref release/vX.Y -f version=X.Y.Z -f android=… -f ios=… -f macos=… -f windows=…` |
| Watch | `gh run watch <id> --exit-status` |

## Common mistakes

- **Passing `X.Y` as the version.** The workflow needs the three-part build
  version from the artifacts; `2.17` fails validation.
- **Dispatching on `dev`.** The `prod-store` environment rejects every ref
  but `release/*`, so the store jobs fail before doing anything.
- **Skipping the tested-build question**, or pre-selecting platforms for the
  user. The only source of truth is the user's answer in this session.
- **Promoting Windows from a stale run.** The pending submission is the last
  uploaded package, not necessarily the one from the run you looked at.
