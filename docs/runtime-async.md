# Runtime Async (.NET 11)

## Status

**Not enabled, and blocked.** Runtime async is available in .NET 11 and we have a working,
toggleable implementation prepared in ActualLab.Fusion, but it **must not be turned on** for
any assembly a Blazor WebAssembly app can reach — which, given Fusion's dependency closure,
means none of them.

**Blazor WebAssembly still runs on Mono in .NET 11, and Mono cannot execute runtime-async
IL.** This was confirmed empirically, not just from documentation: a Blazor WASM app built
against a `runtime-async=on` library fails at the first call, and one of the three observed
failure modes is a **silent hang with no exception and no console output**. See
[Blazor WebAssembly is the blocker](#blazor-webassembly-is-the-blocker).

The concern is portability, not correctness: we ship one `net11.0` assembly per package to
every consumer, and there is no way for a consumer to opt out at runtime.

Investigated 2026-07-31 against SDK `11.0.100-preview.6.26359.118`.

## What runtime async actually is

Normally the C# compiler rewrites an `async` method into a state machine type: a nested
`struct` implementing `IAsyncStateMachine`, plus an `[AsyncStateMachine]` attribute on the
method, plus `AsyncTaskMethodBuilder` calls to drive it.

With runtime async, the compiler stops doing that. It emits the method body more or less
literally, marks the method with `MethodImplOptions.Async` (`0x2000`), and inserts calls to
`System.Runtime.CompilerServices.AsyncHelpers.Await` at suspension points. The runtime then
owns suspension and resumption.

The difference is directly observable. Same source, compiled both ways:

| | classic | runtime async |
|---|---|---|
| `MethodImplAttributes` | `IL` (`0x0`) | `Async` (`0x2000`) |
| `[AsyncStateMachine]` on the method | present | absent |
| nested state-machine types generated | 2 | 0 |
| IL of a trivial `async Task<int>` | state machine + `MoveNext` | 59 bytes, calls `AsyncHelpers.Await` |

The upside is fewer allocations — no state machine box per call, no `Task` allocated at
an await boundary between two runtime-async methods — plus cleaner stack traces, because
the frames are real frames rather than `MoveNext` hops.

## The critical property: it is a compile-time encoding, not a runtime policy

This is the single most important thing to understand before enabling it.

Once a method is compiled with runtime async, **the state machine version does not exist
in the assembly**. There is nothing for a host to fall back to. Consequently there is
**no way to disable runtime async for an application** — you cannot flip it off for an app
that consumes assemblies built with it.

Verified against .NET 11 preview 6:

- `DOTNET_RuntimeAsync=0` — no effect. The `DOTNET_RuntimeAsync` / `UNSUPPORTED_RuntimeAsync`
  switch that existed during the .NET 10 experiment was **removed** once the feature shipped.
- `COMPlus_RuntimeAsync=0` — no effect.
- No CLRConfig knob in `coreclr.dll`; the only `RuntimeAsync` strings there are a metadata
  type name and the type-load error message `"Bad use of RuntimeAsync flag."`
- No `AppContext` switch or `runtimeconfig.json` feature switch in `System.Private.CoreLib`.
- No SDK feature-switch property.
- The only async-related JIT knob is `DOTNET_JitAsyncReuseContinuations`, which tunes
  continuation allocation. It is not an on/off switch.

That the runtime carries a *type-load error* for the flag confirms it is a hard contract
with the runtime, not a hint it may choose to honor.

The nearest thing to an opt-out is per-method and at build time:
`[RuntimeAsyncMethodGeneration(false)]` flips a single method back to a real state machine.
It works even though the attribute is not in the preview 6 reference assemblies — the
compiler matches it by name, so you declare it yourself. **`[module: ...]` is ignored**, so
there is no assembly-wide form.

## The .NET 11 BCL is already runtime-async

Microsoft ships the CoreCLR base class library compiled with runtime async. Measured
directly from the shipped binaries (`shared/Microsoft.NETCore.App/11.0.0-preview.6`):

| Assembly | runtime-async methods | classic state machines |
|---|---|---|
| `System.Private.CoreLib.dll` | 99 | **0** |
| `System.Net.Http.dll` | 117 | 2 |
| `System.Net.Sockets.dll` | 8 | 0 |

**This does not force your code into runtime async.** A runtime-async method has two entry
points sharing one metadata token: the async variant, and a `Task`-returning **thunk** that
behaves exactly like a classic async method. Which one a caller binds to depends on how the
*caller* was compiled.

- Compiled **without** the flag → calls the thunk → gets an ordinary `Task`; your method
  stays a state machine; behavior is as it was on .NET 10.
- Compiled **with** the flag → calls the async variant → no `Task` allocated at the
  boundary, direct suspension. This is where the performance win comes from.

Verified: a probe compiled with `<Features>strict</Features>` awaiting `Task.Yield()` and
`Task.Delay()` — both runtime-async BCL methods — runs correctly and remains a state machine.

### Consequence: two different risk axes

Worth separating these, because they point in opposite directions.

**Correctness risk from the runtime-async machinery is partly unavoidable on .NET 11.**
The BCL already runs it, so bugs in that machinery can reach us whether or not we set the
flag. The `SuppressFlow` bug below fires inside
`AsyncHelpers.RuntimeAsyncTaskCore.DispatchContinuations` — a BCL frame. Not enabling the
flag shrinks the surface (our own methods stay classic) but grants no immunity.

**Portability risk is entirely under our control.** Whether a shipped assembly contains IL
that a given runtime can *load at all* depends purely on our flag. Microsoft sidesteps this
by building the BCL separately per runtime flavor. We ship one assembly for all consumers
and have no equivalent mechanism.

The second axis is what decides the question for ActualLab.Fusion.

## Why we haven't enabled it

### 1. Portability — one assembly, no consumer escape hatch

Fusion packages ship a single `net11.0` assembly consumed by servers, Blazor WebAssembly,
and MAUI on iOS/Android. If any of those runtimes cannot execute runtime-async IL, the
failure is unfixable by the consumer, because there is no runtime opt-out (above). The only
remedy would be shipping separate package flavors.

Runtime support status:

| Target | Runtime | Runtime async |
|---|---|---|
| Server / desktop | CoreCLR | Supported; BCL itself uses it |
| MAUI iOS / Android (Voxt), default | CoreCLR + partial R2R + interpreter — see [Native AOT](./native-aot.md) | Works — but see the Mono caveat below |
| MAUI iOS / Android with `UseMonoRuntime=true` | Mono | **Throws `PlatformNotSupportedException`** |
| Blazor WebAssembly | Mono — hard-coded, not overridable | **Fails.** See below |
| Mono generally | Mono | **Blocked** — [dotnet/runtime#124489](https://github.com/dotnet/runtime/issues/124489), closed 2026-07-10 |

Mono's `AsyncHelpers` is guarded `#if CORECLR || NATIVEAOT`; the Mono branch is ten methods
that all `throw new PlatformNotSupportedException("Runtime Async is not supported on this
platform.")`. That string is present in the shipped Mono browser-wasm, iOS and Android
`System.Private.CoreLib` and absent from the CoreCLR ones. The master tracking issue
[#109632](https://github.com/dotnet/runtime/issues/109632) states for .NET 11, verbatim:
*"Mono runtime is not supported."*

#### Blazor WebAssembly is the blocker

Blazor WASM is still Mono in .NET 11, and it is not configurable.
`Microsoft.NET.Runtime.WebAssembly.Sdk/.../BrowserWasmApp.targets` hard-codes
`<WasmAppRuntimeFlavor>Mono</WasmAppRuntimeFlavor>` **unconditionally, with no override
condition**. A `CoreCLR` branch exists in `WasmApp.Common.targets` but nothing in the browser
path can select it, and only a Mono browser-wasm runtime pack ships. Browser-CoreCLR
([#121511](https://github.com/dotnet/runtime/issues/121511)) still has "Build and run simple
Blazor app" and "Complete Blazor support" unchecked, and targets .NET 12 for a real
transition.

The wasm BCL confirms the intent — scanned across the whole Mono browser-wasm pack:

| Assembly set (11.0.0-preview.6) | runtime-async methods |
|---|---|
| Mono browser-wasm `System.Private.CoreLib.dll` | **0** / 40,975 |
| Mono browser-wasm, all other 182 assemblies | **0** |
| CoreCLR win-x64 `System.Private.CoreLib.dll`, same build | 99 / 44,149 |

**Observed failure modes.** A `net11.0` library compiled with `runtime-async=on` was
referenced from a `blazorwasm-empty` app, published, and loaded in Chrome. The assembly
**loads fine** — failure is at first call:

| Method shape | Result |
|---|---|
| `async Task<int>` with `await Task.Delay(1)` | `PlatformNotSupportedException: Runtime Async is not supported on this platform.` |
| `async Task<int>` with no await, **awaited** | **Silent hang.** App stuck at `Loading...`, no console output, no exception. Blazor never started. |
| Same, called without awaiting | `NullReferenceException` — the `Task` was never produced |

The second row is the dangerous one. Mono ignores the `0x2000` flag entirely — its
`tabledefs.h` has no constant for it — so it executes IL that `ret`s a raw `int` from a
method whose metadata return type is `Task<int>`. Undefined behavior, no diagnostic.

This also shows [#124489](https://github.com/dotnet/runtime/issues/124489)'s "block Mono
comprehensively" did **not** fully land: it was closed as completed on 2026-07-10 but its
timeline carries no linked PR and no referencing commit. What exists in preview 6 is only the
`AsyncHelpers` throw-stubs, which catch a method only if it actually reaches a suspension
helper.

**Scope note:** because the failure is per-assembly at execution time, excluding only
`ActualLab.Fusion.Blazor` would not help. Every package in the closure a Blazor WASM app can
reach — `ActualLab.Core`, `ActualLab.Rpc`, `ActualLab.Fusion`, `ActualLab.Interception`, … —
must stay off it. That is effectively all of Fusion.

#### The mobile situation is subtler than "CoreCLR is the default"

MAUI moved to CoreCLR by default on all platforms in .NET 11 Preview 4, and runtime async
does work there. Verified locally in the installed SDK — `Microsoft.iOS.Sdk.net11.0_26.5`'s
`Xamarin.Shared.Sdk.props:43` sets `<UseMonoRuntime …>false</UseMonoRuntime>` unconditionally,
where the net10 iOS SDK on the same machine still defaults it to `true`. Android's
`_AndroidRuntime` resolves to `CoreCLR` unless `PublishAot` (→ NativeAOT) or
`UseMonoRuntime=true` (→ MonoVM). Runtime async works under all of NativeAOT
([#124101](https://github.com/dotnet/runtime/issues/124101)), crossgen2/R2R — NativeAOT ([#124101](https://github.com/dotnet/runtime/issues/124101)),
crossgen2/R2R, and the CoreCLR interpreter that iOS relies on for everything not
precompiled. The iOS/Android CoreCLR `System.Private.CoreLib` ships ~93 runtime-async
methods, so every .NET 11 mobile app already executes them.

The problem is that **Mono is still first-class and selectable**. `UseMonoRuntime=true`
remains supported on iOS and Android (only macOS hard-errors), Mono runtime packs still ship,
and **Android API levels 21–23 are only supported on Mono**. A consumer that opts into Mono
resolves our plain `net11.0` asset and dies at the first `await` with
`PlatformNotSupportedException` — at runtime, in production.

There is **no compile-time signal**. Verified: a library built with `runtime-async=on` for
`net11.0;net11.0-ios;net11.0-android` compiles with **0 warnings on all three** and emits
`0x2000` methods in all three.

#### Microsoft does not enable it for their own mobile libraries

This is the strongest single signal. From `src/libraries/Directory.Build.targets` in
dotnet/runtime:

```xml
<RuntimeAsyncSupported Condition="'$(TargetOS)' != 'browser'
  and '$(TargetOS)' != 'wasi'
  and '$(TargetOS)' != 'android'
  and '$(TargetsAppleMobile)' != 'true'
  and '$(RuntimeFlavor)' != 'Mono'">true</RuntimeAsyncSupported>
```

Introduced by [#124076](https://github.com/dotnet/runtime/pull/124076) ("Disable runtime
async feature on Apple mobile") and unchanged since 2026-03-28. The same predicate gates the
**test** projects, so library-level runtime async is **not CI-validated on Android or Apple
mobile at all**. Measured in the shipped preview 6 packs — note the zeros:

| Runtime pack (11.0.0-preview.6) | `System.Private.CoreLib` | `System.Net.Http` |
|---|---|---|
| Desktop CoreCLR | 99 | 117 |
| `Runtime.ios-arm64` (CoreCLR) | 93 | **0** |
| `Runtime.NativeAOT.ios-arm64` | 93 | **0** |
| `Runtime.android-arm64` (CoreCLR) | 93 | **0** |
| `Runtime.Mono.ios-arm64` | **0** | **0** |
| `Runtime.Mono.android-arm64` | **0** | **0** |

Enabling it for all of Fusion would put us **ahead of Microsoft's own risk posture** on
mobile.

### 2. `ExecutionContext.SuppressFlow()` may not be honored — and we depend on it

[dotnet/runtime#122052](https://github.com/dotnet/runtime/issues/122052), filed 2025-11-29,
**still open**. The runtime's own test suite catches it:

```
System.Net.Sockets.Tests.ExecutionContextFlowTest.SocketAsyncEventArgs_ExecutionContextFlowsAcrossSendAsyncOperation(suppressContext: True, ...)
  Assert.Equal() Failure: Expected: 0, Actual: 42
    at System.Runtime.CompilerServices.AsyncHelpers.RuntimeAsyncTaskCore.DispatchContinuations[T,TOps](T task)
```

Fusion leans on suppression in core paths, via `ExecutionContextExt.TrySuppressFlow()`:

| Call site | Runs on |
|---|---|
| `ActualLab.CommandR/Internal/Commander.cs` | every outermost command |
| `ActualLab.Core/Async/WorkerBase.cs` | every worker, unless `FlowExecutionContext` |
| `ActualLab.Core/Async/AsyncChain.cs`, `AsyncChainExt.cs` | chain isolation |
| `ActualLab.Core/Async/BatchProcessor.cs` | batch dispatch |
| `ActualLab.Core/Collections/PruningCache.cs` | cache pruning |

`ExecutionContextExt` also reaches further into `ExecutionContext` than most code does — it
`UnsafeAccessor`s the private `Default` static field and re-enters through
`ExecutionContext.Run`. Fusion is unusually exposed to exactly these semantics.

The failure mode is quiet. Leaked context across `Commander`'s outermost boundary would not
crash; it would surface as `AsyncLocal` state bleeding between commands. Fusion keeps a lot
in `AsyncLocal`: `CommandContext`, `ComputeContext`, `RpcInboundContext`, `RpcPeer`,
`DbHub`, `AsyncLock`.

### 3. The feature is still stabilizing

Of roughly 90 `runtime-async`-labelled issues in dotnet/runtime, most are closed — but the
severe ones closed recently:

| Issue | Closed |
|---|---|
| [#129813](https://github.com/dotnet/runtime/issues/129813) — miscompiles `Stream.ReadAsync(Memory<byte>)` tail-call forwarder into an NRE (regression) | 2026-06-29 |
| [#125071](https://github.com/dotnet/runtime/issues/125071) — `ExecutionEngineException` with `runtime-async=on` | 2026-06-20 |
| [#126735](https://github.com/dotnet/runtime/issues/126735) — object rooting issue | 2026-06-10 |
| [#125042](https://github.com/dotnet/runtime/issues/125042) — JIT bad codegen | 2026-03-02 |

Miscompiles and GC-rooting bugs were being fixed a month before this was written. Normal for
a preview; less comfortable when the consequences land on every downstream consumer.

Open items also worth tracking:

- [#120855](https://github.com/dotnet/runtime/issues/120855) — known de-optimizing patterns.
  Synchronous `Task`-returning wrappers (including `Task.FromResult`) are currently a
  *pessimization* under runtime async, and large structs may be repeatedly copied to and from
  the heap when suspension is frequent. Both patterns are common in Fusion.
- [#119842](https://github.com/dotnet/runtime/issues/119842) — custom awaiters get boxed on suspension.
- [#118319](https://github.com/dotnet/runtime/issues/118319) (milestone *Future*) — `MethodInfo.GetMethodBody()` fails on runtime-async methods. **Verified not applicable to us**: Fusion has no `GetMethodBody` call sites. Re-check if that changes.
- [#118074](https://github.com/dotnet/runtime/issues/118074) (milestone *12.0.0*) — `MethodImpl.Async` unsupported on dynamic methods. **Verified not applicable**: Fusion emits `DynamicMethod`s (`ActivatorExt`, `MemberInfoExt`, `ArgumentList`, `RuntimeCodegen`) for constructors and member access, never for async methods.
- [#127951](https://github.com/dotnet/runtime/issues/127951) — `AsyncProfilerTests.RuntimeAsync_*` fail on android-arm64 and tvos-arm64. Diagnostics only — the async code ran, only EventPipe assertions failed.
- [#127766](https://github.com/dotnet/runtime/issues/127766) / [#122547](https://github.com/dotnet/runtime/issues/122547) — `DiagnosticMethodInfo.Create(new StackFrame())` degrades in runtime-async methods on the interpreter (iOS) and NativeAOT.
- [dotnet/roslyn#77954](https://github.com/dotnet/roslyn/issues/77954) — **Edit-and-Continue / hot reload is not implemented** for runtime async. A real day-to-day cost when debugging into affected assemblies.
- [dotnet/roslyn#84061](https://github.com/dotnet/roslyn/issues/84061) — enabling runtime async silently ignores custom `[AsyncMethodBuilder]`. We don't use that attribute, so no impact today.

There is **no** open correctness or crash bug specific to arm64, arm32, Apple platforms, or
Android — the remaining mobile issues are diagnostics-only.

### Code size is not a concern

Worth recording because it's the objection people expect. For async-dense assemblies runtime
async **shrinks** output: [#125148](https://github.com/dotnet/runtime/issues/125148) measures
NativeAOT `System.Net.Http.Functional.Tests` at **-3.6 MB (-9.9%)**; async-light assemblies
grow ~+0.4%. [#125541](https://github.com/dotnet/runtime/issues/125541) measures R2R IL total
**-4.33%**. Fusion is async-dense, so expect neutral-to-better. On iOS it may also be a
straight speed win — per jkotas, classic async "falls back to JIT a lot" because generic
instantiations can't be R2R-compiled, and on iOS "falls back to JIT" means "falls back to the
interpreter." Runtime async doesn't have that problem.

## The prepared implementation

Present in ActualLab.Fusion's `src/Directory.Build.props`, currently the only change:

```xml
<PropertyGroup>
  <UseRuntimeAsync Condition="'$(UseRuntimeAsync)' == ''">true</UseRuntimeAsync>
  <Features Condition="$(UseRuntimeAsync) and $(TargetFramework.StartsWith('net11'))">$(Features);runtime-async=on</Features>
</PropertyGroup>
```

Scoped to `src/` (libraries only, not tests) and gated on the net11 TFM, so
`ActualLab.Generators`' `netstandard2.0` leg and the `*.NetFx` projects'
`net48`/`net472` legs are excluded automatically.

`UseRuntimeAsync` is **not** an SDK-defined property — it is ours, so there is no collision.
MSBuild reads environment variables as properties, so it can be set either way:

```powershell
dotnet build ActualLab.Fusion.sln                             # on (current default)
dotnet build ActualLab.Fusion.sln -p:UseRuntimeAsync=false    # off, one build
$env:UseRuntimeAsync = 'false'                                # off, whole shell
```

The full Fusion solution builds clean with it on — 0 errors, unchanged warning count, and
nothing hit the compiler's `ERR_UnsupportedFeatureInRuntimeAsync`. It applied broadly:
99 runtime-async methods in `ActualLab.Core`, 67 in `ActualLab.Fusion`, 58 in `ActualLab.Rpc`,
55 in `Ext.Services`, 54 in `EntityFramework`, 33 in `Redis`.

## How to check whether an assembly uses runtime async

There is no tooling for this, so read the metadata directly. A method is runtime-async iff
its `MethodImplAttributes` has bit `0x2000` set:

```csharp
using var pe = new PEReader(File.OpenRead(path));
var md = pe.GetMetadataReader();
var count = md.MethodDefinitions
    .Select(md.GetMethodDefinition)
    .Count(m => (m.ImplAttributes & (MethodImplAttributes)0x2000) != 0);
```

At runtime, reflection shows the same thing — `method.MethodImplementationFlags` reports
`Async`, and `[AsyncStateMachine]` is absent.

## The conditional path, if we ever want it anyway

The safe shape is the one dotnet/runtime already uses: enable runtime async **only for a
server/desktop-only TFM**, and ship non-runtime-async assets for everything Mono can reach.

That is harder than it sounds. The plain `net11.0` asset is exactly what a Blazor WASM app
resolves, so it cannot carry runtime-async IL — meaning the flag would have to be gated on a
`net11.0-browser`-style TFM split, or a second server-only build of the entire package set.
Fusion currently avoids multi-targeting altogether (`UseMultitargeting` is off by default),
so this is a significant packaging change for a benefit we haven't yet measured.

Consumers on `net10.0-*` are safe either way — they're on Mono but resolve our `net10.0`
asset.

The cheapest tripwire to monitor is the `RuntimeAsyncSupported` condition in
`src/libraries/Directory.Build.targets` in dotnet/runtime. When Microsoft drops the
`android` / `TargetsAppleMobile` / `browser` exclusions, that's the signal that the platform
story has caught up.

## If we revisit this

The decision hinges on portability, so the order is:

1. The Blazor WASM answer is already no, and it is not a matter of degree — a single-asset
   package cannot serve both Mono and CoreCLR consumers. Revisit only when browser-CoreCLR
   ships for Blazor ([#121511](https://github.com/dotnet/runtime/issues/121511), targeting
   .NET 12), or when we're willing to multi-target. No measured speedup changes this.
2. Only then benchmark. The A/B is `-p:UseRuntimeAsync=false` against the default, rebuilding
   between runs — the artifacts path does not vary by the flag, so a stale
   `artifacts/bin/*/debug/` is easy to mistake for a fresh one.
3. Expect the win where there are many async frames per operation. Per the RpcBenchmark
   notes, the RPC `Sum` test is transport/OS-bound and will not show it; compute and
   `ComputedState` paths are the ones with enough suspension to matter.
4. Weigh [#122052](https://github.com/dotnet/runtime/issues/122052) separately. It is a
   correctness question about our `SuppressFlow` usage, and it is not answered by a benchmark.

## See also

- [Native AOT](./native-aot.md) — the MAUI runtime/AOT story, including the .NET 11 move off Mono.
