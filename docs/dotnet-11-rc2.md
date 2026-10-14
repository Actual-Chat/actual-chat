# .NET 11 RC2 toolchain

## Version pins

| Component | Version |
|---|---|
| .NET SDK (`global.json`) | `11.0.100-rc.2.26504.105` |
| Runtime / ASP.NET Core packages | `11.0.0-rc.2.26504.105` |
| MAUI | `11.0.0-rc.2.26505.5` |
| Android workload | `37.2.0-rc.2.84` |
| iOS / Mac Catalyst / macOS workloads | `27.0.12212-net11-rc.2` |

`.github/workloads-net11-rc2.json` contains the manifest versions from
Microsoft's published workload set `11.0.100-rc.2.26509.2`. CI uses this
rollback file for native builds to avoid mixing SDK and workload bands.
Existing `net11.0` target frameworks do not need a major-version change.

EF Core and `dotnet-ef` intentionally remain on `11.0.0-rc.1.26425.128`:
the latest published Npgsql EF provider, `11.0.0-rc.1.1`, requires that exact
EF version. Upgrade the provider, EF packages, and tool together when a
compatible provider is published. Historical migrations are not rewritten.

MAUI RC2 still depends on the RC1 Blazor WebView package. The application's
explicit `Microsoft.AspNetCore.Components.WebView` reference is retained
so its JavaScript assets match the centrally pinned RC2 Blazor packages.

## iOS and Mac Catalyst

Build hosts need **Xcode 27.0 and macOS 26.6 or later**. CI selects Xcode 27.0;
Apple build jobs use GitHub's `xcode-27` preview runner because `macos-26`
does not include Xcode 27. This runner meets the host OS requirement, but
its preview status can mean queueing delays or image instability. Do not bypass SDK/Xcode version validation to use Xcode 26.6.

Xcode 27 requires the UIScene lifecycle. Both application manifests now
register `SceneDelegate` using MAUI's default scene configuration, with
multiple scenes disabled. iOS window styling, activation/background state,
and user-activity routing run through the scene delegate. Background handling
retains the background-task assertion while suspend handlers release file
locks. Cold-launch activities and URL contexts are delivered after MAUI
creates the scene window; warm deliveries use the normal scene callbacks.

iOS explicitly keeps `DynamicCodeSupport=false`. RC2 no longer derives this
switch from `UseInterpreter=false` under CoreCLR, so leaving the old property
would silently re-enable dynamic library paths. The runtime issue #130840 is
closed, but the fix still needs validation on our device/component paths before
removing this precaution. This switch does not disable CoreCLR's interpreter.
Mac Catalyst retains the SDK's dynamic-code default.

Firebase initialization, registration tokens, and silent remote notifications
remain application-level callbacks. The iOS share extension and Swift widget /
notification-service extensions are not UIWindowScene applications and do not
need the main application's scene manifest.

The scene delegates stay in the application's platform folders: they are
native entry points, and the iOS one depends on application-specific call
routing and splash styling, not a reusable Core/Core.Server service.

Local installation on a compatible Mac:

```sh
dotnet workload install maui ios maccatalyst --from-rollback-file .github/workloads-net11-rc2.json
dotnet workload --info
xcodebuild -version
sw_vers -productVersion
```

## Android

The RC2 workload includes API 37.0, 37.1, and 37.2 bindings; the default
unversioned `net11.0-android` target still selects API 37.0. It requires
**JDK 21**, Android SDK build-tools **37.0.0**, and the corresponding SDK
platform. The dependency installer selects the packages required by the
workload instead of relying on the runner's preinstalled Android SDK.
Android Release explicitly enables R8 with `AndroidLinkTool=r8`, selects
`AndroidR8ObfuscationMode=private-members`, and `proguard.cfg` no longer
globally disables obfuscation. This enables RC2's
supported Java private/package-private member renaming and R8 optimization;
Java class names and public/protected members remain protected by SDK rules
for managed/JNI interoperability. It does not obfuscate managed .NET assemblies.

Existing library keep rules remain in place, so protected libraries can still
reduce Play Console's measured obfuscation percentage. Validate a Release AAB
in Play Console before claiming the low-obfuscation warning is resolved. If the
percentage remains too low, review and narrow individual library keep rules
against their consumer rules and device tests rather than enabling unrestricted
class/member renaming.

Android's application minimum remains API 28; iOS remains 16.4 and
Mac Catalyst remains 17.0. Build-tool upgrades do not raise these app minimums.

CI explicitly selects Microsoft JDK 21 and runs `InstallAndroidDependencies`
with the runner's Android SDK and Java paths before packaging. For a local
Android build, install the pinned workload and run that target with your
`AndroidSdkDirectory` and `JavaSdkDirectory`, accepting SDK licenses as needed.

## Native validation before release

On compatible build hosts, build both Debug and Release packages and test:

- iOS / Mac Catalyst cold launch and foreground/background transitions.
- iOS suspension with open local stores, then resume without file-lock crashes.
- Cold and warm universal links, auth URL callbacks, and call intents.
- Firebase token registration, foreground alerts, and silent dismissal pushes.
- iOS share extension, notification-service extension, and Live Activities widget.
- Android launch, WebView JavaScript interop, auth/passkeys, Firebase pushes,
  audio recording/playback, and background resume with R8 obfuscation enabled.
- Play Console's obfuscation percentage for the actual Android Release AAB.
- Release R2R packaging on both platforms; Apple builds must keep full R2R.

## Local Android build result

The RC2 Android Release package build succeeded with R8 enabled and debug-key
signing. The generated AAB's `BUNDLE-METADATA/com.android.tools/r8.json`
confirms obfuscation, optimization, and shrinking are enabled. It reports
`noObfuscationPercentage=93.57`, or about **6.43% obfuscated**. Enabling R8
alone is therefore not enough to establish that the Play warning is resolved;
review the broad library keep rules next and remeasure after device testing.

### First keep-rule cleanup

Removed only the blanket `androidx.**` and `com.google.**` keeps. Dependency
consumer rules, generated binding rules, SDK JNI protections, and existing
MAUI/Sentry/TensorFlow/audio rules remain intact.

Using the same build command as the baseline:

| R8 metadata metric | Baseline | First cleanup |
|---|---:|---:|
| Obfuscation | 6.43% | 25.64% |
| Optimization | 8.62% | 58.60% |
| Shrinking | 8.96% | 61.78% |
| DEX bytes | 23,068,796 | 9,580,388 |
| AAB bytes | 61,412,398 | 54,640,386 |

These builds report XA4304 for a missing generated
`proguard_project_references.cfg`. The RC2 SDK looks under a nested
`android-arm64/` intermediate directory, but `UseArtifactsOutput` places the
inner build's generated file in a sibling RID-suffixed directory. The CI
multi-RID invocation still needs this path mismatch addressed before release.
Do not suppress the warning or supply an empty file: it contains JNI keep rules.

A subsequent build with an explicit `-r android-arm64` correctly consumes the
generated rules and has no XA4304 warning. That device-test candidate reports
**27.36% obfuscation, 60.09% optimization, and 64.30% shrinking**; DEX size is
10,844,508 bytes. These figures are not a controlled comparison to the earlier
baseline because this invocation also includes the previously missing rules.

Device-test build command (debug-key signing, dev flavor):

```sh
dotnet build src/dotnet/App.Maui/App.Maui.csproj -c Release -f net11.0-android \
  -r android-arm64 '-p:TargetFrameworks="net11.0-android;net11.0"' \
  -p:AndroidSdkDirectory=/home/undead/android-sdk \
  -p:JavaSdkDirectory=/home/undead/java-sdk \
  -p:AndroidKeyStore=false -p:AndroidBuildApplicationPackage=true -m:1
```

The candidate APK is under
`artifacts/bin/App.Maui/release_net11.0-android_android-arm64/`.
### Device deployment and first regression

Deployed the debug-key-signed dev APK to the attached ARM64 CPH2747 with
`adb install -r`, preserving app data. Windows ADB works directly; this session's
configured WSL TCP connection to the Windows ADB server times out.

The first candidate crashes during SecureStorage initialization:
`EncryptedSharedPreferences$PrefKeyEncryptionScheme.AES256_SIV` is missing.
Generated binding rules preserve the enum class but have an empty member block;
R8 removes the constant because its managed JNI access is invisible to Java.
The subsequent `BlazorWebViewApp.Current` initialization failure is secondary.

Added a targeted rule preserving public static enum fields only under
`androidx.security.crypto`. After rebuilding and reinstalling, the app logs
`BlazorWebViewApp ready`, remains alive, and keeps MainActivity in the foreground
without the previous missing-field crash. R8 metadata remains **27.34%
obfuscation, 60.08% optimization, and 64.30% shrinking**, with 10,849,736 DEX bytes.

This is not a complete functional pass: the visible UI remains a blank purple
screen, and Firebase rejects the local Android client configuration. WebView
rendering, auth/passkeys, push delivery, audio, and background controls still need
validation. The packages have not been uploaded to Play Console.

## Sources

- [.NET 11 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json)
- [Published RC2 workload set](https://www.nuget.org/packages/Microsoft.NET.Workloads.11.0.100-rc.2/11.100.0-rc.2.26509.2)
- [Apple RC2 requirements and scene breaking change](https://github.com/dotnet/macios/releases/tag/dotnet-11.0.1xx-rc2-12212)
- [GitHub Xcode 27 preview runner](https://github.com/actions/runner-images/issues/14404)
- [MAUI scene lifecycle integration](https://github.com/dotnet/maui/pull/38601)
- [Android RC2 release](https://github.com/dotnet/android/releases/tag/37.2.0-rc.2.84)
- [Android RC2 toolchain configuration](https://github.com/dotnet/android/blob/37.2.0-rc.2.84/Configuration.props)
