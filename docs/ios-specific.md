# iOS-specific behavior

Things that behave differently on iOS than on every other platform we ship, and the
reasons why. Mac Catalyst shares most of them — both are Apple targets with the same
runtime constraints.

## Policy: every platform keeps a JIT or an interpreter

**We are not yet ready to ship NativeAOT-style production builds** — meaning no JIT
*and* no interpreter. We are close, and it's where we want to end up, but until every
reflection and serialization path is covered statically, each platform must keep one of
the two available. Turning the safety net off early doesn't make us AOT-ready; it just
moves the failures in front of users.

In practice: Android and Windows have a JIT. **iOS currently has neither** — see
*The interpreter is off, and why* below — so on iOS the policy is enforced the hard
way, by making sure nothing needs dynamic code in the first place. Native AOT builds
have neither by definition either; that's why they're not production yet, and why
[CodeKeeper](./native-aot.md) coverage is the work that gets us there.

## The interpreter is on, but only with Blazor excluded from R2R (interim)

Two settings that must move together, both in `App.Maui.csproj`:

```xml
<UseInterpreter>true</UseInterpreter>
<PublishReadyToRunExclude Include="Microsoft.AspNetCore.Components.dll" />
```

Turning the interpreter on alone **crashes the app**. On .NET 11 preview 6 the CoreCLR
interpreter faults dispatching an open-instance delegate over a virtual or interface
method when the caller is R2R-compiled ([dotnet/runtime#130840][r]) — four consecutive
`0x8BADF00D` watchdog kills, main thread:

```
InterpExecMethod / ExecuteInterpretedMethod
  CID_VirtualOpenDelegateDispatchWorker      <- native fault in interpreted code
    EEPolicy::LogManagedCallstackForSignal
      CallStackLogger::PrintStackTrace
        TypeString::AppendMethodImpl         <- >5s symbolicating -> process killed
```

[r]: https://github.com/dotnet/runtime/issues/130840

The symptom is deceptive: the app paints its UI and then ignores touches. That's not our
code hanging — the main thread is stuck in CoreCLR's *own* fatal-error handler,
symbolicating a managed stack on device, until the watchdog fires. Pull the reports with
`idevicecrashreport -u <udid> -k <dir>`; `faultingThread` points at thread 0.

**Two separate callers reach it, and they needed different fixes.**

The first was ours: `MetadataExt` builds `Action<IHasMetadata, MetadataBag>` from an
interface property setter via Fusion's `GetSetter`, which is an open-instance delegate
over an interface method. Fixed upstream in **ActualLab.Fusion 14.2.50** — `MemberInfoExt`
now declines that shortcut on affected Apple configurations and emits codegen instead.

The second is **Blazor's own**, and no library fix can reach it:

```
ComponentBase.StateHasChanged -> Renderer.ProcessRenderQueue
  -> ComponentState.SupplyCombinedParameters -> FusionComponentBase.SetParametersAsync
    -> ComponentProperties.SetProperties -> <SetProperties>g__SetProperty|4_0  -> FAULT
```

`ComponentProperties` builds an open-instance `Action<TTarget,TValue>` for every component
parameter setter, so it's on the path of *every* Blazor render. Notably no `[Parameter]` in
this repo or in Fusion is declared `virtual`, and the component that faulted
(`VirtualList<TItem>`) is `sealed` — the stack shows `VirtualList_1<System__Canon>`, so
shared-generic instantiation appears to be what reaches the virtual-dispatch path.

The fix is to stop that caller being R2R-compiled. The issue states the fault needs an
R2R caller and passes when the code is interpreted, so excluding
`Microsoft.AspNetCore.Components.dll` from the R2R image resolves it — verified on device.
It costs ~17MB off the R2R image (94.5MB → 77.7MB) and makes Blazor's parameter path
interpreted.

**Remove both together when the runtime fix ships.** Dropping the exclusion alone brings
the crash back; dropping `UseInterpreter` alone silently disables dynamic code again.

### This is interim, not the answer

It works and it's verified on device, but it buys correctness with an unmeasured perf cost:
Blazor's parameter-assignment path — which runs on every render — is now interpreted rather
than precompiled. Startup and render-heavy interactions on iOS should be measured against a
build without the exclusion before anyone treats this as settled.

Alternatives considered, roughly in order of how attractive they look:

- **Do the parameter assignment ourselves.** `FusionComponentBase.SetParametersAsync` already
  decides whether `base.SetParametersAsync` runs at all (it short-circuits via
  `ComponentInfo.ShouldSetParameters`), and `ComponentInfo` already holds per-parameter
  metadata built on `MemberInfoExt.GetGetter`. Adding setters there and assigning directly
  would route every Fusion component through the helper that 14.2.50 already guards, and skip
  `ComponentProperties` entirely — no build flags, no perf cliff on the render path. Covers
  only components deriving from `FusionComponentBase`, not plain `ComponentBase` ones.
- **Non-generic component subclasses.** The faulting component was generic
  (`VirtualList_1<System__Canon>`), so re-declaring parameters on a non-generic descendant
  would move the setters off shared-generic code. Verbose, and only fixes what you convert.
- **Patch Blazor's setter cache by reflection.** `ComponentProperties` caches `WritersForType`;
  injecting setters that use a closed delegate or `PropertyInfo.SetValue` would fix every
  component at once. `IPropertySetter` is `internal`, so it needs a dynamic assembly with
  `IgnoresAccessChecksTo` — the most general and the most fragile.
- **Wait for the runtime fix.** Milestoned 11.0.0, root cause already pinned upstream. If it
  lands before we ship .NET 11, all of the above becomes moot.

## How this went wrong once

`RuntimeFeature.IsDynamicCodeSupported` was **`false`** on iOS between the .NET 11 sweep
and its fix. You can see the flip in the two runtimeconfigs:

```
net10 iOS:  "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": true
net11 iOS:  "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported": false
```

**It was our setting, not a platform limit.** Apple forbids JIT, but CoreCLR on iOS
ships an interpreter, and the macios SDK decides dynamic-code support purely from two
MSBuild properties (`Xamarin.Shared.Sdk.targets`):

```xml
<DynamicCodeSupport Condition="... '$(MtouchInterpreter)' == '' And '$(UseInterpreter)' != 'true'
                               And ('$(_PlatformName)' == 'iOS' Or 'tvOS' Or 'MacCatalyst')">false</DynamicCodeSupport>
```

We used to set both — `UseInterpreter=true` with `MtouchInterpreter=-ActualChat`, i.e.
ActualChat assemblies AOT'd and the rest interpreted. The .NET 11 sweep removed them as
Mono-era cleanup. They are **not** Mono-era: the net11 SDK still reads them, and dropping
them flipped the flag. We set `UseInterpreter=true` again and then had to remove it —
the interpreter crashes; see above.

The nuance is that `MtouchInterpreter` survives only as a build-time input on CoreCLR —
its actual method filter is discarded. See
[Which `Mtouch*` properties still do something](#which-mtouch-properties-still-do-something-under-coreclr)
below.

**Android was unaffected.** It permits JIT and was already on CoreCLR before the sweep
(`UseCoreClr` + `UseMonoRuntime=false`), so `IsDynamicCodeSupported` stayed `true` — which
is why the failure showed up on iOS alone.

**Mac Catalyst is an open question.** macOS allows JIT, so it probably doesn't need the
interpreter — but the SDK condition above lists `MacCatalyst`, so it likely gets
`DynamicCodeSupport=false` too and would fail the same way. Unverified. If it does, prefer
setting `DynamicCodeSupport=true` there over paying for an interpreter it doesn't need.

### What breaks: MessagePack formatters

`AppMessagePackResolverSettings` registers the Reflection.Emit-based fallbacks
conditionally:

```csharp
if (RuntimeFeature.IsDynamicCodeSupported) {
    standardResolvers.Add(DynamicUnionResolver.Instance);
    standardResolvers.Add(DynamicObjectResolver.Instance);
}
```

`DynamicObjectResolver` manufactures a formatter at runtime for any type that lacks
one. With dynamic code off it isn't registered, so a type without a source-generated or
attribute formatter fails — and the failure is loud but indirect:

```
INF [FCE] FormatterNotRegisteredException, ActualChat.Media.Size2D is not registered
          in resolver: ActualChat.Serialization.Internal.AppMessagePackResolver
INF [FCE] MessagePackSerializationException, Failed to deserialize
          ApiArray<ActualChat.Video.VideoStreamInfo>
INF [FCE] SerializationException, Cannot deserialize inbound call arguments.
ERR [ActualLab.Rpc.RpcClientPeer] Failed to process inbound message: …
ERR [ErrorBoundary] SerializationException: Cannot deserialize inbound call arguments.
ERR [ErrorBarrier] ErrorBarrier VideoPanel activated, error count = 1
```

One missing formatter takes down every RPC call whose argument graph reaches that
type, and surfaces to the user as an error barrier — not as anything mentioning
serialization. Read the chain bottom-up: the barrier names the *component*, the first
`[FCE]` names the *type*.

Two consequences worth internalising:

- **The bug is never iOS-specific, only iOS-visible.** The same gap exists on Android;
  it's silently paid for with runtime codegen per type. Every one of these is also a
  Native AOT blocker on every platform. `Size2D` had carried `[MessagePackObject]` and
  `[Key(...)]` all along — `DynamicObjectResolver` was simply generating it at runtime,
  on every platform, unnoticed.
- **It scales badly.** A single missing formatter produced ~23k first-chance
  exceptions in one short session, because every affected RPC message throws.

Still fix the type properly — give it a real formatter (source-generated,
`[MessagePackFormatter]`, or an explicit registration). The interpreter keeps a missing
formatter from reaching users; it doesn't make the gap go away, and every such gap is
still a Native AOT blocker. Getting them all covered is what makes NativeAOT builds
shippable.

## Which `Mtouch*` properties still do something under CoreCLR

The `Mtouch*` prefix is a Xamarin-era name, which makes the whole family look like
dead weight now that Apple targets run CoreCLR. Most of it isn't. The net11 iOS SDK
still imports the legacy Xamarin targets — `Xamarin.Shared.Sdk.targets:2913-2916`
sets `_TargetsDirectory` to `tools/msbuild/` and imports `Xamarin.iOS.CSharp.targets`
for every `iOSExecutableProject`, which chains to `tools/msbuild/Xamarin.Shared.props`
and `Xamarin.Shared.targets`. So a property that appears nowhere under `targets/` can
still be live.

Audited against `packs/Microsoft.iOS.Sdk.net11.0_26.5/26.5.11720-net11-p6/`.
`UseMonoRuntime` defaults to `false` there (`Xamarin.Shared.Sdk.props:43`), so CoreCLR
is the path that matters.

| Property | Under CoreCLR |
|---|---|
| `MtouchLink` | **Live.** The trimming knob — see *Other deltas*. `Xamarin.Shared.Sdk.Trimming.props:79` |
| `MtouchExtraArgs` | **Live.** Feeds `AppBundleExtraOptions` → `ParseBundlerArguments`. `tools/msbuild/Xamarin.Shared.props:117` |
| `MtouchDebug` | **Live.** Sets `_BundlerDebug`, which drives `DebuggerSupport`, `UseSystemResourceKeys`, `-DDEBUG`. `Xamarin.Shared.Sdk.targets:120` |
| `MtouchNoSymbolStrip` | **Live.** Native symbol strip, incl. the CoreCLR/R2R frameworks. `tools/msbuild/Xamarin.Shared.props:122`, `Xamarin.Shared.targets:3132` |
| `MtouchNoDSymUtil` | **Live.** Same shape, for `dsymutil`. `tools/msbuild/Xamarin.Shared.props:130` |
| `MtouchHttpClientHandler` | **Live.** Picks `NSUrlSessionHandler` / `CFNetworkHandler`; an invalid value is error E7152. `Xamarin.Shared.Sdk.targets:147,659,661,797` |
| `MtouchSdkVersion` | **Live**, obscure — pins the Xcode SDK version. Never reassigned at target time. `tools/msbuild/Xamarin.Shared.props:42` |
| `MtouchArch` | **Live as an error only** — build fails telling you to use `RuntimeIdentifier`. `Xamarin.Shared.Sdk.targets:1283-1287` |
| `MtouchInterpreter` | **Discarded** by the linker; build-time side effects remain. See below |
| `MtouchUseLlvm` | **Mono-only.** Only reaches `mono_use_llvm`, written `if (app.XamarinRuntime == XamarinRuntime.MonoVM)` |
| `MtouchFloat32` | **Mono-only.** An AOT-compiler argument (`Application.AotFloat32`) |
| `MtouchEnableSGenConc` | **Mono-only.** An SGen GC setting |

### `MtouchInterpreter` is accepted, then thrown away

The linker discards it outright — `tools/dotnet-linker/LinkerConfiguration.cs` in
`dotnet/macios`:

```csharp
if (Application.XamarinRuntime != XamarinRuntime.MonoVM && Application.UseInterpreter) {
    Application.Log (4, "The interpreter is enabled, but the current runtime isn't MonoVM. The interpreter settings will be ignored.");
    Application.UnsetInterpreter ();
}
```

Verbosity 4 — you will never see that message in a normal build. So the *method
filter* half of `MtouchInterpreter=-ActualChat` has been inert since we moved to
CoreCLR; there is no CoreCLR equivalent of Mono's per-assembly interpreter selection
anywhere in the SDK.

What survives is the MSBuild side, and only one of those matters. Every other use is
gated on Mono: `_RunAotCompiler` sits inside `<PropertyGroup Condition="'$(UseMonoRuntime)' == 'true'">`
(`Xamarin.Shared.Sdk.targets:1316-1320`), which in turn gates the `sealer` trimmer
optimization (`:770`) and `_IsDedupEnabled` (`:1359`); `_MonoComponent hot_reload`
(`:488`) is only read by `_ComputeMonoComponents`, itself Mono-gated (`:495`); the
hot-reload error (`:2861`) and both `Trimming.props` defaults (`:70,72`) carry
`'$(UseMonoRuntime)' == 'true'` explicitly.

The exception is the one that bit us — `Xamarin.Shared.Sdk.targets:158` has **no**
runtime guard, so setting `MtouchInterpreter` still suppresses `DynamicCodeSupport=false`
on CoreCLR. That is the entire remaining effect of the property on our builds, and
`UseInterpreter=true` reaches the same line (and aliases to `MtouchInterpreter=all` at
`tools/msbuild/Xamarin.Shared.props:190`).

### Setting process environment variables in the app bundle

There *is* a supported mechanism, and the SDK uses it on the CoreCLR path itself —
`Xamarin.Shared.Sdk.props:260-262`:

```xml
<ItemGroup Condition="'$(UseMonoRuntime)' == 'false' And '$(_PlatformName)' != 'macOS' And '$(PublishReadyToRun)' != 'true'">
    <_BundlerEnvironmentVariables Include="DOTNET_ReadyToRun" Value="0" />
</ItemGroup>
```

Adding to that item group is the whole recipe:

```xml
<ItemGroup>
    <_BundlerEnvironmentVariables Include="DOTNET_SomeKnob" Value="1" />
</ItemGroup>
```

The equivalent through `MtouchExtraArgs` is `--setenv:KEY=VALUE`, parsed by
`ParseBundlerArguments` and appended to the same item group
(`tools/msbuild/Xamarin.Shared.targets:2294-2309`). Prefer the item — it skips
shell-style argument splitting and a first-separator (`:` or `=`) split of the value.

The chain from there, none of it runtime-gated:

1. `Xamarin.Shared.Sdk.targets:683` emits
   `@(_BundlerEnvironmentVariables -> 'EnvironmentVariable=Overwrite=%(Overwrite)|%(Identity)=%(Value)')`
   into `_CustomLinkerOptions`.
2. `dotnet-linker`'s `LinkerConfiguration` parses it into `Application.EnvironmentVariables`.
3. `GenerateMainStep` (unconditional) has `tools/common/Target.cs` write a literal
   `setenv("KEY", "VALUE", 1)` per entry into the generated `main.<arch>.mm`, inside
   `xamarin_setup_impl()` — last, *"so that the app developer can override any other
   environment variable we set"*.
4. `runtime/monotouch-main.m` calls `xamarin_setup()` early in `xamarin_main`, **before**
   `xamarin_bridge_setup()` and `xamarin_vm_initialize()`.

So the variables are in the process environment before CoreCLR initialises, which is
exactly why the SDK's own `DOTNET_ReadyToRun=0` works. Whether any particular
`DOTNET_*` knob is honoured is then a dotnet/runtime question — macios only guarantees
it is set in time.

Two lookalikes that are **not** this:

- `RuntimeEnvironmentVariable` → `MlaunchEnvironmentVariables`
  (`Microsoft.Sdk.Mobile.targets:99,115`) only applies to `dotnet run` / mlaunch
  launches. Nothing is written into the bundle.
- The `-setenv=` launch argument parsed in `runtime/monotouch-main.m` is a
  debugger/mlaunch channel, not a shipping one.

Nothing writes environment variables into `Info.plist`; the generated `main.mm` is the
only bundle-baked path.

## Other deltas

**`_ExportSymbolsExplicitly=true` is required.** CoreCLR won't resolve
`DllImport("__Internal")` unless the main executable exports symbols; Mono resolved it
regardless. Without it the app dies on its first P/Invoke with
`DllNotFoundException '__Internal'` out of `Foundation.NSObject`'s cctor. Set in
`App.Maui.csproj`; **not** set on `App.Maui.IosShareExt`, so if the share extension
ever fails to launch on a P/Invoke, start there.

**Trimming is spelled `MtouchLink`.** The macios SDK always runs the trimmer and
rejects an explicit `PublishTrimmed` outright (*"iOS projects do not support setting
'PublishTrimmed'"*), so the knob is `MtouchLink` — `None` / `SdkOnly` / `Full`.

**ReadyToRun is always full, never partial.** MAUI's `_MauiPublishReadyToRunPartial`
(which appends `--partial` to crossgen2) is gated on
`TargetPlatformIdentifier == 'android'`, so it can't affect Apple targets.

**The launch surface has to be painted explicitly.** The `UIWindow` and the root view
controller's view are white by default and show for a frame between the launch
storyboard and WebKit's first paint — see [Splash screens](./splash-screen.md).

**The app bundle is two apps.** `ActualChat.app` plus
`PlugIns/ActualChat.App.Maui.IosShareExt.appex` (`chat.actual.dev.app.share`), signed
with its own provisioning profile. A global `-p:CodesignProvision=…` therefore breaks
the build — it applies to every project in the graph and the extension's bundle id
won't match.

## Debugging on a device

Nothing in the app writes a log file on iOS, and `devicectl … --console` streams
nothing for it. Two channels together give full coverage:

**.NET side** — the device syslog carries our `ILogger` output, including every
`[FCE]`:

```bash
idevicesyslog -u <udid> | grep -aE 'ActualChat\[[0-9]+\] <'
```

Note the filter: lines with a parenthesised subsystem (`ActualChat(WebKit)`,
`ActualChat(CoreHaptics)`) are system noise; ours have none.

**JS side** — the WebView console via `ios_webkit_debug_proxy`.
`MauiWebView.MaciOS.cs` sets `webView.Inspectable = true` on iOS 16.4+ with no
`#if DEBUG` guard, so Release builds are inspectable. The app must be **foregrounded**
(backgrounded, the page list is empty — indistinguishable from Web Inspector being
off), and iOS 26's WebKit needs commands wrapped in `Target.sendMessageToTarget`
rather than flat CDP.

Reach for the syslog first: a failure that renders as an error barrier is almost
always .NET-side, and the JS console will show nothing useful.
