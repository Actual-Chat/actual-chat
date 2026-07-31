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

In practice: Android and Windows have a JIT, and iOS keeps the interpreter on
(`UseInterpreter=true` in `App.Maui.csproj`). Native AOT builds have neither by
definition — that's exactly why they're not production yet, and why
[CodeKeeper](./native-aot.md) coverage is the work that gets us there.

### How this went wrong once

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
them flipped the flag. `UseInterpreter=true` is now set again for iOS.

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
