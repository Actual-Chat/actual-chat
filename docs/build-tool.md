# The `b` Build Tool

`b` is the command line for building, running and packaging Voxt. It's a
git-style command tree — type a command, get help at every level, or run it with
no arguments for an interactive menu.

Run it from the repo root:

```bash
./b.cmd          # Windows
./b.ps1          # anywhere (pwsh)
```

The first run compiles the tool; after that it starts in ~100 ms and only
rebuilds when its own sources change.

> Extending `b` — adding commands, options or a new group — is covered in
> [`build/README.md`](https://github.com/Actual-Chat/actual-chat/blob/dev/build/README.md).
> This page is about *using* it.

## Getting around

```bash
b                    # interactive menu
b --help             # also -?, /?, /h, /help - works at every level
b app --help         # help for a group
b app run --help     # help for one command
b tree               # the whole command tree at a glance
b tree -o            # ... including every argument and option
```

Every command takes `--dry-run`, which prints the exact commands it *would* run
and then stops. Use it whenever you want to know what a flag actually does:

```bash
$ b app run android --release --prod --dry-run
Would run:
  $ npm run build:Release
  $ dotnet publish src/dotnet/App.Maui -noLogo -c Release -f net11.0-android
      -p:IsDevMaui=false -p:AndroidSigningKeyPass=*** -p:AndroidSigningStorePass=***
  -> artifacts/publish/App.Maui/release_net11.0-android/chat.actual.app-Signed.apk
  $ adb install -r artifacts/publish/.../chat.actual.app-Signed.apk
  $ adb shell monkey -p chat.actual.app -c android.intent.category.LAUNCHER 1
```

Secrets (the Android signing passwords) are masked in that output; they're still
passed to the real process.

## Interactive mode

Running `b` with no arguments opens a menu built from the same command
definitions the CLI uses, so it always matches `--help`.

- `← back` is the first entry in every list, but the cursor starts on the entry
  below it.
- `b` or `←` goes back, `r` runs the command you're editing, `Esc` quits — no
  Enter needed.
- The header above the menu shows the command you're building and the commands
  it will actually run, updating in place as you toggle options.
- If a combination is invalid, the header says why and `<run>` is replaced by
  `<run - blocked>`.

It's the fastest way to discover flags: pick a command, walk its options, and
watch the real command line assemble itself.

## Apps

```bash
b app build <platform>     # build only
b app install <platform>   # build + install on the device
b app run <platform>       # build + launch (+ install where the platform needs it)
b app pack <platform>      # build the store package
```

`<platform>` is `android`, `ios`, `windows` or `mac` (`macos` is a synonym).

| | build | install | launch |
|---|---|---|---|
| `b app build` | ✓ | | |
| `b app install` | ✓ | ✓ | |
| `b app run` | ✓ | ✓¹ | ✓ |

`--launch` / `-l` and `--no-launch` override the default either way, and
launching implies installing — so `b app build android -l` is the same as
`b app run android`, and `b app run android --no-launch` is the same as
`b app install android`.

¹ Except an unpackaged Windows build, which is launched straight from its `.exe`
and so installs nothing — see [Windows](#windows).

Common flags:

| Flag | Effect |
|---|---|
| `-r`, `--release` | shorthand for `--configuration Release` |
| `--prod` | build the production app (`IsDevMaui=false`) — talks to voxt.ai instead of dev.voxt.ai |
| `--aot` | build with Native AOT |
| `--simulator` | iOS only — target a simulator instead of a connected device |
| `--catalyst` | macOS only — the Mac Catalyst app instead of the default AppKit one (`net11.0-macos`) |
| `--no-web` | skip the npm web asset build |
| `--package` | Windows — build an MSIX package instead of the unpackaged app; implied on the other platforms |
| `--publish` / `--no-publish` | force `dotnet publish` / `dotnet build` |

Examples:

```bash
b app run android                        # dev build on a connected device
b app run android --release --prod       # production build, signed
b app run ios --simulator                # iOS simulator
b app run windows                        # Windows, unpackaged - just runs the .exe
b app run windows --package              # ... packaged (MSIX): installs, then launches
b app run windows --release --aot        # Windows, Native AOT
b app run mac                            # native AppKit app; --catalyst for Mac Catalyst
b app pack android --prod                # the .aab you upload to Play Console
b app pack macos --universal             # the AppKit .pkg as CI builds it (arm64 + x64)
```

`b app pack` runs the same publish target CI uses, then prints where the
artifact landed:

```
Output: artifacts/publish/App.Maui/release_net11.0-android/chat.actual.app-Signed.aab (142.7 MB)
```

`--prod` Android builds need `ActualChat_AndroidSigningKeyPass` and
`ActualChat_AndroidSigningStorePass` in the environment.

### Windows

Windows builds are **unpackaged by default**: `app run windows` launches
`artifacts/.../ActualChat.exe` directly and blocks until the app exits. Deploying is
`app install`'s job — `app run` never registers anything on its own.

`--package` builds an MSIX instead, so the app gets a package identity — that's
what toast notifications, the `voxt-dev://` protocol handler and the startup task
need. A packaged app can't be started from its `.exe`, so `--package` implies
deployment: `app run windows --package` registers the build and then activates it
by package family name.

`app install windows` implies `--package` — an unpackaged build has nothing to
deploy, so the flag would be the only thing that made the command meaningful.
`--aot` can't be packaged at all, so `app install windows --aot` is rejected.
On Android, iOS and macOS packaging is the only mode, so `--package` is accepted
there but warns that it's redundant.

> **`--package` is currently broken.** It registers `<output>/AppX/AppxManifest.xml`,
> and no `AppX` layout is produced — a packaged `dotnet build` writes a manifest to
> the output root and stages only the Windows App Runtime dependency under
> `obj/.../MsixContent/`. The step fails with `Not found: ...AppX/AppxManifest.xml`
> rather than doing anything harmful. Getting a loose layout probably needs
> `GenerateAppxPackageOnBuild` or an explicit layout target; until then use the
> default unpackaged run.

The identity is whatever the checked-in `Platforms/Windows/Package.appxmanifest`
says — `ActualChatInc.ActualChat.Local`, which doesn't collide with an installed
store Voxt. One caveat: `app pack` (via `AppxManifestGenerator`) rewrites that
tracked file in place to the store identity and doesn't restore it, so after a
pack the next `app run` registers over the store app until you revert the file.

Since the registration points into `artifacts/`, a rebuild updates the installed
app in place; re-running `app run` re-registers it. Stop the running app before
rebuilding — otherwise MSBuild can't overwrite `ActualChat.exe`, packaged or not.

The default build passes `WindowsPackageType=None` and launches
`artifacts/.../ActualChat.exe` directly; that run blocks until the app exits,
while a packaged one returns as soon as the app is activated. `--aot` forces the
unpackaged path regardless: Native AOT publishes a self-contained exe rather than
an MSIX.

### iOS and macOS

These delegate to `scripts/run-ios.sh`, `scripts/run-ios-simulator.sh`,
`scripts/run-mac.sh` (the default AppKit app) and `scripts/run-maccatalyst.sh`
(`--catalyst`), which handle device detection, certificate install and
the codesigning workarounds. They build, install and launch as one unit, so
`b app install ios` is rejected rather than silently launching the app — use
`b app run ios` or `b app build ios`.

## Server

```bash
b server run                              # from source, ASPNETCORE_ENVIRONMENT=Development
b server run --log                        # ... writing the dev log to tmp/server.log
b server run --log tmp/other.log          # ... somewhere else
b server run --published                  # run the build in artifacts/publish
b server run --urls http://localhost:7086 # a second instance on another port
b server run --open                       # open https://local.voxt.ai/ once it's up
b server publish                          # publish into artifacts/publish
b server loop                             # the edit-run-restart loop
```

`--log` resets the file first, so each run starts clean. It defaults to
`tmp/server.log` in the repo root and takes an optional path to override that.
`--open` follows `--urls` when you've set one, and also accepts an explicit URL.

`b server loop` hands off to `server-loop.ps1` — the loop lives in that script
because `/server-loop` and other tooling reference it directly.

## Build targets

Anything `b` doesn't recognize as a command is treated as a build target, so the
existing pipeline is reachable unchanged:

```bash
b build                  # the default build
b restore
b unit-tests
b clean
b watch
b --list-targets         # everything available
```

Available targets:

```
build                clean               clean-dist          clean-tests
default              e2e-tests           generate-version    integration-tests
integration-tests-chat                   integration-tests-core
integration-tests-mlsearch               integration-tests-users
maui                 nightly-tests       npm-build           npm-install
pack-android         pack-ios            pack-mac            pack-maccatalyst
pack-win             restore             restore-tools       slnf
slow-tests           tests               unit-tests          watch
```

These accept the target-level options: `--configuration`, `--is-dev-maui`,
`--use-native-aot`, `--dumps`, `--parallel`, `--verbose`, `--list-tree`, and the
rest of the Bullseye flags. CI and the `Dockerfile` call this same path through
`run-build.cmd`.

## Full command tree

Generated by `b tree -o`:

```
b
├── app ...  Build & run the Voxt client apps (MAUI)
│   ├── run  Build, install & launch the app on a device
│   │   ├── <PLATFORM> (Android|Ios|Windows|Mac)  android | ios | windows | mac (or macos)
│   │   ├── -c|--configuration = Debug  Debug or Release
│   │   ├── -r|--release  Shorthand for --configuration Release
│   │   ├── --prod  Build the production app (IsDevMaui=false): voxt.ai instead of dev.voxt.ai
│   │   ├── --aot  Build with Native AOT
│   │   ├── --simulator  iOS only: target a simulator instead of a connected device
│   │   ├── --publish  Force dotnet publish (the default for Release)
│   │   ├── --no-publish  Force dotnet build
│   │   ├── --no-web  Skip the npm web asset build
│   │   ├── --package     Windows: build an MSIX package instead of the unpackaged app; implied elsewhere
│   │   ├── -l|--launch  Launch the app after installing it (the default for 'app run')
│   │   ├── --no-launch  Don't launch the app (the default for 'app build' and 'app install')
│   │   └── --dry-run  Print the commands that would run, without running them
│   ├── install  Build & install the app on a device, without launching it
│   │   └── (same arguments and options as `app run`)
│   ├── build  Build (or publish) the app, without installing it
│   │   └── (same arguments and options as `app run`)
│   └── pack  Build the store package (App Store / Play Store / MS Store)
│       ├── <PLATFORM> (Android|Ios|Windows|Mac)  android | ios | windows | mac (or macos)
│       ├── --prod  Pack the production app (IsDevMaui=false): voxt.ai instead of dev.voxt.ai
│       ├── --aot  Build with Native AOT (currently wired for ios only)
│       └── --dry-run  Print the commands that would run, without running them
├── server ...  Build & run the Voxt server
│   ├── run  Run the server from source, or from artifacts/publish
│   │   ├── -c|--configuration = Release  Debug or Release - ignored with --published
│   │   ├── --published  Run the published build from artifacts/publish instead of from source
│   │   ├── --log [PATH]  Write the dev log to PATH (default: tmp/server.log)
│   │   ├── --urls <URLS>  Override ASPNETCORE_URLS, e.g. http://localhost:7086
│   │   ├── --open [URL]  Open the app in a browser once the server is up (default: https://local.voxt.ai/)
│   │   └── --dry-run  Print the commands that would run, without running them
│   ├── publish  Publish the server into artifacts/publish
│   │   ├── -c|--configuration = Release  Debug or Release
│   │   └── --dry-run  Print the commands that would run, without running them
│   └── loop  Run server-loop.ps1 - the npm build / dotnet build / server run loop
│       ├── -c|--configuration = Release  Debug or Release
│       └── --dry-run  Print the commands that would run, without running them
├── tree  Print the whole command tree
│   └── -o|--options  Also show arguments and options
└── [TARGETS]  Bullseye targets to run, e.g. build, watch, restore, pack-ios
```

Run `b tree -o` yourself for the authoritative version — it's read from the
command definitions, so it can't go stale.

## Passing extra arguments

Everything after `--` goes through to the underlying tool:

```bash
b app build android -- -p:SomeMsBuildProperty=true
b server run -- --my-server-arg
```

## Related

- [`build/README.md`](https://github.com/Actual-Chat/actual-chat/blob/dev/build/README.md)
  — how `b` is built and how to add commands to it.
- [Running Voxt](./running-voxt.md) — environment setup, infrastructure services
  and the scripts `b` doesn't cover yet.
