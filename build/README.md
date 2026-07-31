# `build` — the `b` command line

This project is two things stacked on each other:

- **A Bullseye target graph** (`Program.cs`) — the build/test/publish pipeline CI
  runs: `restore`, `build`, `unit-tests`, `publish-android`, `publish-ios`, …
- **A Spectre.Console.Cli command tree** (`Commands/`) — the git-style `b`
  command line developers use day to day, plus an interactive UI.

Run it with `./b.cmd` (Windows) or `./b.ps1` (anywhere). The shim rebuilds this
project only when its sources changed, so a warm `b` starts in ~100 ms.

```
b                          # interactive menu
b --help                   # also -?, /?, /h, /help - at every level
b tree -o                  # the whole command tree, with arguments and options
b app run android
b app pack ios --prod
b clean                    # unknown first word -> a Bullseye target
```

The app commands differ only in how far down the pipeline they go, and `--launch`
/ `--no-launch` override the default either way:

| | build | install | launch |
|---|---|---|---|
| `b app build` | yes | | |
| `b app install` | yes | yes | |
| `b app run` | yes | yes | yes |
| `b app pack` | store package, no install/launch | | |

iOS and Mac Catalyst delegate to `scripts/run-ios*.sh` / `scripts/run-macos.sh`,
which build, install and launch as one unit — so `b app install ios` is rejected
rather than silently launching.

The server commands:

```
b server run                              # dotnet run, Release, ASPNETCORE_ENVIRONMENT=Development
b server run --log                        # ... plus ActualChat_DevLog -> tmp/server.log (reset first)
b server run --log tmp/other.log          # ... to somewhere else
b server run --published                  # run artifacts/publish/App.Server/release instead
b server run --urls http://localhost:7086 # a second instance on another port
b server run --open                       # ... and open it in a browser 10s in
b server publish                          # publish into artifacts/publish
b server loop                             # hand off to server-loop.ps1
```

`b server loop` deliberately shells out rather than reimplementing the loop —
`/server-loop` and other tooling reference that script directly.

## Layout

| File | What it is |
|---|---|
| `Program.cs` | Bullseye targets. `RunTargets` is the old entry point; `RunTarget` runs a single target in-process. |
| `Commands/CliApp.cs` | Entry point. Builds the command tree, captures the command model, starts the UI. |
| `Commands/TargetsCommand.cs` | The **default** command: anything that isn't a known verb is treated as Bullseye targets. This is what keeps `run-build.cmd` (Dockerfile, CI) working. |
| `Commands/PlanCommand.cs` | Base class for commands that shell out. |
| `Commands/CommandPlan.cs` | The plan model: `RunStep`, `ActionStep`, `OutputStep`, plus `CliContext`. |
| `Commands/CommandBrowser.cs` | The interactive UI. |
| `Commands/TreeCommand.cs` | `b tree`. |
| `Commands/ParameterExt.cs` | Reads names, defaults and enum choices off the parser's parameter metadata. |
| `Commands/TypeRegistrar.cs` | Minimal DI so commands can take `CliContext` in their constructor. |

## The plan pipeline

A command doesn't run processes directly. It **describes** what it would run,
and the base class decides what to do with that description:

```csharp
public sealed class MyCommand(CliContext context) : PlanCommand<MyCommand.Settings>(context)
{
    protected override CommandPlan GetPlan(Settings settings)
        => new CommandPlan()
            .AddRun("npm", ["run", "build:Debug"])
            .AddRun(Utils.FindDotnetExe(), ["build", "src/dotnet/App.Server"])
            .AddOutput("artifacts/bin/App.Server/debug/ActualChat.App.Server.exe");
}
```

`ExecutionMode` decides the rest:

| Mode | Trigger | Behavior |
|---|---|---|
| `Run` | default | Prints each step, then runs it. |
| `Explain` | `--dry-run` | Prints the steps, runs nothing. |
| `Validate` | the interactive UI | Binds + validates settings and builds the plan, silently. Nothing runs. |

This is why the UI can tell you a command won't work *before* you run it: it
re-dispatches the real command in `Validate` mode after every edit, so the
errors it shows are the parser's and the command's own — never a second copy of
the same rules.

### Step kinds

- `AddRun(exe, args, workingDir)` — a process. `RequiredPath` on the step makes
  it fail with a clean message if an input is missing.
- `AddAction(description, fn)` — something in-process (e.g. a Bullseye target).
  The description is what gets printed.
- `AddOutput(path)` — reports where the artifact landed, as a repo-relative path
  with its size. The path may be a glob (`*.pkg`). Put it *before* a launch
  step, so the location is still visible after the app takes over the console.
- `AddSecret(value)` — masks that value everywhere the plan is printed.

## Adding a command

1. Write a settings class. Options and arguments are declared with attributes;
   these drive the help text, the tree, and the interactive field editors, so
   fill in `[Description]` and `[DefaultValue]`.

   ```csharp
   public sealed class Settings : PlanSettings
   {
       [CommandArgument(0, "<PLATFORM>")]
       [Description("android | ios | windows | macos")]
       public AppPlatform Platform { get; init; }

       [CommandOption("-r|--release")]
       [Description("Shorthand for --configuration Release")]
       public bool IsRelease { get; init; }

       public override ValidationResult Validate()
       {
           if (IsRelease && IsDebug)
               return ValidationResult.Error("--release and --debug are mutually exclusive.");

           return ValidationResult.Success();
       }
   }
   ```

   Put **every** cross-flag rule in `Validate()`. That's the one place the CLI
   and the interactive UI both read.

2. Derive from `PlanCommand<TSettings>` and implement `GetPlan`. Throw
   `WithoutStackException` for problems you can only detect while building the
   plan (a missing env var, an unsupported combination) — the message is shown
   without a stack trace, and the UI picks it up as a validation error.

3. Register it in `CliApp.CreateApp`:

   ```csharp
   c.AddBranch("web", b => {
       b.SetDescription("Build the web assets");
       b.AddCommand<WebBuildCommand>("build")
           .WithDescription("Build the TypeScript/CSS bundle")
           .WithExample("web", "build", "--release");
   });
   ```

That's it — help, `b tree`, and the interactive menu all pick it up
automatically. Nothing else needs updating.

### Enum arguments

Declare the argument as an enum and both the help and the UI get the choice list
for free (`b app run <PLATFORM>` prompts with a picker rather than free text).
Parsing is case-insensitive.

### Notes

- `PlanSettings.ExtraArgs` holds everything after `--`, filled in before
  `GetPlan` runs. Append it to your tool's argument list to give users an escape
  hatch.
- Prefer *delegating* to an existing script over reimplementing it. `b server
  loop` shells out to `server-loop.ps1` because `/server-loop` and other tooling
  reference that script directly; `b app run ios` calls the `scripts/run-ios*.sh`
  files because they carry real Apple-toolchain logic. Scripts `b` delegates to
  live in `scripts/` and resolve their own paths via `REPO_ROOT`, so they work
  both from `b` and when a human runs them directly.
- Prefer *calling a Bullseye target* over re-deriving publish flags. `b app pack`
  runs the same `publish-*` target CI does, so the store artifacts can't drift
  from what ships.

## Interactive UI conventions

- **`← back` / `← quit` is always the first entry**, but the cursor starts on the
  entry below it — you never have to move off "back" to pick something.
- **Shortcuts**: `b` or `←` goes back, `r` runs the command being edited. Both
  work without Enter.
- **The header is the only thing that redraws.** The logo, the `b …` command
  being built, and its effective plan sit above the prompt and update in place;
  moving through options doesn't scroll the console. Command *output* is left on
  screen, with a keypress before returning to the menu.

These come from `Prompts.cs` and `CommandBrowser.WriteHeader` — new commands get
them for free. `Prompts.Show` drives the cursor position and shortcuts by
wrapping `IAnsiConsole.Input`, since Spectre exposes no API for either.

## How the interactive UI stays in sync

Spectre builds a command model from the registrations and hands it only to the
help provider. `ModelCapturingHelpProvider` in `CliApp.cs` intercepts it, and
`CommandBrowser` renders its menus from that model. So there is no second list of
commands to maintain — the UI can't drift from the parser.

One escape hatch: `ICommandParameter` doesn't expose the parameter's CLR type, so
`ParameterExt.GetEnumType()` reads it off the concrete parameter by reflection.
If that ever breaks, enums degrade to free-text input; nothing else is affected.

## Back-compat rules

`run-build.cmd` is referenced by the `Dockerfile` and ~15 CI steps, and
`ef-migrations.cmd` by the `Dockerfile`. Both go through this project. When
changing the CLI:

- Keep `TargetsCommand` as the default command, and keep its option names
  (`--configuration`, `--is-dev-maui`, `--use-native-aot`, `--dumps`, the
  `--list-*` flags).
- Don't name a branch after an existing Bullseye target — the default command
  only gets first-word args that aren't registered commands.

Quick check after any change:

```bash
./b.ps1 publish-android --configuration Release --is-dev-maui "true" --dry-run
dotnet run --project build -c Release -- generate-version --dry-run
```
