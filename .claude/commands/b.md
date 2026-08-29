---
allowed-tools: Bash, Read
description: Run the `b` build tool — build/install/run/pack the apps, run or publish the server, or run any Bullseye target. Use whenever a task needs building, running, packaging, or a build target.
---

# /b — the build tool

Runs `b`, this repo's build CLI, with `$ARGUMENTS`.

```bash
pwsh -NoProfile -File ./b.ps1 $ARGUMENTS
```

If `$ARGUMENTS` is empty, run `pwsh -NoProfile -File ./b.ps1 tree -o` and show
the user what's available instead of guessing at a command.

Prefer `b` over the root `*.cmd` scripts — most of them have been replaced by it.
Full usage guide: [`docs/build-tool.md`](../../docs/build-tool.md). How the tool
itself is built and extended: [`build/README.md`](../../build/README.md).

## Rules

1. **Never launch interactive mode.** A bare `b` opens a menu that needs a TTY.
   You don't have one — it falls back to printing the tree, which is harmless
   but useless. Always pass an explicit command.
2. **`--dry-run` first** for anything long or destructive. Every command accepts
   it; it prints the exact commands that would run and stops. Show that to the
   user before running the real thing when the operation takes minutes
   (`app pack`, `app run`, `server publish`) or touches a device.
3. **Long-running commands need `run_in_background: true`.** `b server run`,
   `b server loop` and `b watch` never return on their own. `b app run` blocks
   too once the app launches.
4. **Don't run `b server run` while `/server-loop` is active** — the loop owns
   the dotnet process and they will fight over the port. Check
   `tmp/server-loop.log` first; see `/server-loop`.
5. **Read the plan output.** Commands print each step before running it, so on
   failure the last `$ ...` line tells you exactly which process failed.
6. **Secrets are masked** in printed plans (`-p:AndroidSigningKeyPass=***`) but
   still passed to the real process. Don't try to "fix" that.

## The command tree

Regenerate with `pwsh -NoProfile -File ./b.ps1 tree -o` — it's read from the
command definitions, so it can't go stale. Current shape:

```
b
├── app ...                Build & run the Voxt client apps (MAUI)
│   ├── run <PLATFORM>     build + launch (+ install where needed)
│   ├── install <PLATFORM> build + install
│   ├── build <PLATFORM>   build only
│   └── pack <PLATFORM>    the store package (App Store / Play Store / MS Store)
├── server ...             Build & run the Voxt server
│   ├── run                from source, or --published
│   ├── publish            into artifacts/publish
│   └── loop               hands off to server-loop.ps1
├── tree                   print this tree (-o for arguments and options)
└── [TARGETS]              anything else = Bullseye targets
```

`<PLATFORM>` is `android` | `ios` | `windows` | `macos` (case-insensitive).

### `app run` / `app install` / `app build` options

| Option | Effect |
|---|---|
| `-c`, `--configuration` | `Debug` (default) or `Release` |
| `-r`, `--release` | shorthand for `--configuration Release` |
| `--prod` | production app (`IsDevMaui=false`) — voxt.ai, not dev.voxt.ai |
| `--aot` | Native AOT |
| `--simulator` | iOS only — simulator instead of a connected device |
| `--publish` / `--no-publish` | force `dotnet publish` / `dotnet build` |
| `--no-web` | skip the npm web asset build (much faster when only C# changed) |
| `--package` | Windows only — build an MSIX package instead of the unpackaged app |
| `-l`, `--launch` / `--no-launch` | override whether it launches |

The three commands differ only in how far they go; `--launch` implies install, so
`b app build android -l` == `b app run android`, and `b app run android
--no-launch` == `b app install android`. The exception is an unpackaged Windows
build: it's launched straight from its `.exe`, so `app run windows` installs
nothing.

Windows builds are **unpackaged** by default. `--package` builds an MSIX instead
— that's what gives the app toast notifications, the `voxt-dev://` protocol
handler and the startup task — and since an MSIX can only start through its
registered identity, `--package` deploys before launching. `app install windows`
implies it. **`--package` is
currently broken**: it registers `<output>/AppX/AppxManifest.xml` and no `AppX`
layout is produced, so the step fails with `Not found`. Use the default
unpackaged run. See [`docs/build-tool.md` → Windows](../../docs/build-tool.md#windows).

`app pack` takes only `<PLATFORM>`, `--prod` and `--aot`.

### `server` options

| Command | Options |
|---|---|
| `server run` | `-c` (default `Release`), `--published`, `--log [PATH]` (default `tmp/server.log`), `--urls <URLS>`, `--open [URL]` |
| `server publish` | `-c` (default `Release`) |
| `server loop` | `-c` (default `Release`) |

### Bullseye targets

Any first word that isn't a registered command is treated as a target:

```
build  clean  clean-dist  clean-tests  default  e2e-tests  generate-version
integration-tests  integration-tests-chat  integration-tests-core
integration-tests-mlsearch  integration-tests-users  maui  nightly-tests
npm-build  npm-install  publish-android  publish-ios  publish-maccatalyst
publish-win  restore  restore-tools  slnf  slow-tests  tests  unit-tests  watch
```

These take `--configuration`, `--is-dev-maui`, `--use-native-aot`, `--dumps`,
`--parallel`, `--verbose`, `--list-targets`, `--list-tree`. This is the same path
CI and the `Dockerfile` use via `run-build.cmd` — don't break its option names.

## Examples

```bash
/b unit-tests --configuration Debug
/b app run android --no-web
/b app pack android --prod --dry-run
/b server run --log --open
/b server publish
/b tree -o
```

## Passing arguments through

Everything after `--` goes to the underlying tool:

```bash
/b app build android -- -p:SomeMsBuildProperty=true
/b server run -- --my-server-arg
```

## Reporting back

Report what actually happened, not what was supposed to. On success, quote the
`Output:` line when there is one — the `app` commands (except on iOS/macOS) and
`server publish` print where the artifact landed, as a repo-relative path with
its size. On failure, quote the failing `$ ...` step and the error beneath it.
