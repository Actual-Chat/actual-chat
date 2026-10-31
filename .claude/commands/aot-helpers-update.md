---
allowed-tools: Bash, Read, Edit
description: Regenerate the AOT helper outputs - the XxxAotSource.g.cs files and aothelper.mibc. Takes [gen|mibc|all]; asks when omitted.
---

# /aot-helpers-update — refresh the AOT helper outputs

`App.AotHelper` produces two kinds of output:

| Output | Mode | Consumed by |
|---|---|---|
| `src/dotnet/*/Module/*AotSource.g.cs` | `-g` | the WASM and MAUI AOT builds — a type missing here may be trimmed out |
| `src/dotnet/App.Maui/_Profiling/aothelper.mibc` | `-m` | iOS Release crossgen2 |

Details: [`docs/native-aot.md`](../../docs/native-aot.md#appaothelper).

`$ARGUMENTS` selects what to update:

| Argument | Runs |
|---|---|
| `gen` | `.g.cs` files only (`-g`) |
| `mibc` | `.g.cs` files **and** `aothelper.mibc` (`-g`, then `-m`) — what `update-aot-helpers.cmd` does |
| `all` | same as `mibc` |

`-m` reads the type set the same way `-g` does, so `aothelper.mibc` is only ever rebuilt
after a fresh `-g`; there is no `.mibc`-only choice.

Anything else — stop and say what the valid values are; don't guess.

## When no argument is given

Ask one question, in prose, after saying what each choice costs: `gen` only rewrites the
`.g.cs` files; `mibc` additionally rewrites the binary `aothelper.mibc`, which shows up as a
changed binary in the diff even when nothing in the type set moved. Recommend `gen` when the
change is "I added or removed a component / API / serializable", `mibc` when the iOS profile
should follow.

## Steps

From the project root:

```bash
dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -g
# only for mibc / all
dotnet run --project src/dotnet/App.AotHelper/App.AotHelper.csproj -c Debug -- -m src/dotnet/App.Maui/_Profiling/aothelper.mibc
```

`update-aot-helpers.cmd` runs both back to back; use it for `mibc` / `all`, and the first
command alone for `gen`. Stop at the first non-zero exit.

## Reporting

`git status --short` for the touched files: which `*AotSource.g.cs` changed (and a one-line
sense of what was added or removed from `git diff --stat`), and whether `aothelper.mibc`
changed. If nothing changed, say so — that is a valid outcome.
