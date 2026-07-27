# MAUI Sign-In Without the External Browser — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [`docs/superpowers/specs/2026-07-27-maui-embedded-auth-design.md`](../specs/2026-07-27-maui-embedded-auth-design.md)

**Goal:** Move MAUI web-based sign-in out of the external system browser and into the platform's in-app browser, and make sign-out a plain command with no browser at all — resolving the App Store Guideline 4 rejection.

**Architecture:** The server-side OAuth flow is untouched. Only the browser host changes (`ASWebAuthenticationSession` / Chrome Custom Tabs / Windows browser + protocol activation) and the way control returns to the app (a 302 to a custom `voxt://` scheme instead of the user manually switching back). Native Apple-on-iOS and Google-on-Android paths stay as the first choice. Sign-out becomes a new `NativeAuth_SignOut` command.

**Tech Stack:** .NET 10, .NET MAUI (`Microsoft.Maui.Authentication`), ActualLab.Fusion (Commander/RPC), xUnit + FluentAssertions.

## Global Constraints

- **Read [`docs/CODING_STYLE.md`](../../CODING_STYLE.md) before writing any C#.** This project deviates from standard .NET conventions: no `Async` suffix on async methods, no XML docs on members (`///` on a method/property/field is forbidden), file-scoped namespaces, `var` over explicit types, expression-bodied members.
- **Comments:** default to none. A `//` comment is justified only for a non-obvious invariant, constraint, or workaround. Never restate what the code says.
- **Build with the CI solution filter:** `dotnet build ActualChat.CI.slnf` (it excludes MAUI, which needs extra workloads).
- **MAUI target frameworks are OS-gated** (`App.Maui.csproj:10-12`). In this Linux/Docker environment only `net10.0-android` can be built — `maui-android` is the sole installed workload. `net10.0-ios` and `net10.0-maccatalyst` build only on macOS; `net10.0-windows10.0.22621.0` only on Windows. Tasks touching iOS/macOS/Windows code are compile-verified by review + CI, not locally.
- **App schemes are per build flavor:** dev → `voxt-dev`, prod → `voxt`. Never hardcode either; always go through `MauiSettings.AppScheme` / `Constants.AppSchemes`.
- **Callback URL:** `{AppScheme}://auth-complete` — `auth-complete` is the URI **host**, not a path (there is no third slash).
- **Branch:** `feat/maui-embedded-auth` (already created, holds the spec commit).

---

## File Structure

**Server (new)**
- `src/dotnet/Api/Constants.AppSchemes.cs` — the two custom URL schemes, shared by client and server.
- `src/dotnet/Api/AuthRedirectUrl.cs` — pure redirect-URL allowlist. No ASP.NET dependency, so it is unit-testable.

**Server (modified)**
- `src/dotnet/Users.Service/AuthHelper.cs:258-281` — `IsCloseFlow` sanitizes `redirectUrl`.
- `src/dotnet/Api.Contracts/Users/INativeAuth.cs` — `NativeAuth_SignOut` command + handler declaration.
- `src/dotnet/Users.Service/NativeAuth.cs` — `OnSignOut` handler.
- `src/dotnet/Api.Contracts/Module/ApiContractsAotSource.g.cs` — regenerated, never hand-edited.

**MAUI (new)**
- `src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs` — the in-app browser session. One responsibility: run a URL to completion and report completed/cancelled.
- `src/dotnet/App.Maui/Platforms/Android/WebAuthCallbackActivity.cs` — the Android intent-filter target.
- `src/dotnet/App.Maui/Platforms/Windows/WindowsAppScheme.cs` — `HKCU` protocol registration for the unpackaged app.

**MAUI (modified)**
- `src/dotnet/Maui/MauiSettings.cs` — add `AuthCallbackUrl`, source `AppScheme` from `Constants.AppSchemes`, delete `WebAuth`.
- `src/dotnet/App.Maui/Services/MauiAccountUI.cs` — the whole point of the change.
- `src/dotnet/App.Maui/Module/MauiAppModule.cs:38` — DI registration.
- `src/dotnet/App.Maui/Platforms/Windows/App.Activation.cs:32-38` — forward protocol activations (it currently drops them).
- `src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist:64-68` — drop the `voxt` scheme.
- `src/dotnet/App.Maui/WebView/MauiWebView.Navigation.cs` — delete dead auth-routing code.

**Tests (new)**
- `tests/Users.UnitTests/AuthRedirectUrlTest.cs`
- `tests/Users.IntegrationTests/NativeAuthSignOutTest.cs`

**CI (modified)**
- `.github/workflows/build-test-deploy-dev.yml:684` — add a MacCatalyst plist rewrite mirroring the iOS one.

---

### Task 1: Redirect-URL allowlist

Closes a pre-existing open redirect (`RootServerPage.razor:431` passes an unvalidated query parameter straight to `Response.Redirect`) *before* Task 5 starts sending custom-scheme URLs through it.

**Files:**
- Create: `src/dotnet/Api/Constants.AppSchemes.cs`
- Create: `src/dotnet/Api/AuthRedirectUrl.cs`
- Modify: `src/dotnet/Users.Service/AuthHelper.cs:258-281`
- Test: `tests/Users.UnitTests/AuthRedirectUrlTest.cs`

**Interfaces:**
- Consumes: `HostInfoExt.GetHosts(this HostInfo)` → `IReadOnlySet<string>` (`src/dotnet/Api/HostInfoExt.cs:16`), already resolves to `Constants.Hosts.AllProd` / `AllDev` / `AllLocal` per deployment.
- Produces:
  - `ActualChat.Constants.AppSchemes.Prod` = `"voxt"`, `.Dev` = `"voxt-dev"`, `.All` : `IReadOnlySet<string>` — Task 3 uses these from `MauiSettings`.
  - `ActualChat.AuthRedirectUrl.Sanitize(string? redirectUrl, IReadOnlySet<string> allowedHosts)` → `string?` (the URL if allowed, else `null`).

- [ ] **Step 1: Write the failing test**

Create `tests/Users.UnitTests/AuthRedirectUrlTest.cs`:

```csharp
namespace ActualChat.Users.UnitTests;

public class AuthRedirectUrlTest
{
    private static readonly IReadOnlySet<string> AllowedHosts
        = new HashSet<string>(["voxt.ai", "actual.chat"], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("/chat", true)] // Relative
    [InlineData("/chat?x=1&y=2", true)] // Relative with query
    [InlineData("https://voxt.ai/chat", true)] // Allowed host
    [InlineData("https://ACTUAL.CHAT/chat", true)] // Allowed host, case-insensitive
    [InlineData("voxt://auth-complete", true)] // Prod app scheme
    [InlineData("voxt-dev://auth-complete", true)] // Dev app scheme
    [InlineData("VOXT://auth-complete", true)] // App scheme, case-insensitive
    [InlineData("https://evil.com/x", false)] // Foreign host
    [InlineData("https://voxt.ai.evil.com/x", false)] // Suffix-lookalike host
    [InlineData("//evil.com/x", false)] // Protocol-relative
    [InlineData("/\\evil.com/x", false)] // Backslash protocol-relative
    [InlineData("javascript:alert(1)", false)] // Script scheme
    [InlineData("data:text/html,<script>", false)] // Data scheme
    [InlineData("chat", false)] // Relative but not rooted
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldAllowOnlySafeRedirects(string? redirectUrl, bool isAllowed)
    {
        var result = AuthRedirectUrl.Sanitize(redirectUrl, AllowedHosts);
        if (isAllowed)
            result.Should().Be(redirectUrl);
        else
            result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter FullyQualifiedName~AuthRedirectUrlTest`
Expected: build failure — `AuthRedirectUrl` does not exist.

- [ ] **Step 3: Add the shared scheme constants**

Create `src/dotnet/Api/Constants.AppSchemes.cs`, following the partial-class pattern of `Constants.SessionTemporals.cs`:

```csharp
namespace ActualChat;

public static partial class Constants
{
    // Custom URL schemes the MAUI apps register. Both flavors are listed on every
    // server: MauiPreferences.HostOverride lets a prod-flavor app sign in against dev.
    public static class AppSchemes
    {
        public const string Prod = "voxt";
        public const string Dev = $"{Prod}-dev";

        public static readonly IReadOnlySet<string> All
            = new HashSet<string>([Prod, Dev], StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Write the validator**

Create `src/dotnet/Api/AuthRedirectUrl.cs`:

```csharp
namespace ActualChat;

/// <summary>
/// Allowlist for post-authentication redirect targets, which arrive as
/// untrusted query parameters on the close-flow endpoint.
/// </summary>
public static class AuthRedirectUrl
{
    public static string? Sanitize(string? redirectUrl, IReadOnlySet<string> allowedHosts)
    {
        if (redirectUrl.IsNullOrEmpty())
            return null;
        if (!Uri.TryCreate(redirectUrl, UriKind.RelativeOrAbsolute, out var uri))
            return null;

        if (!uri.IsAbsoluteUri) {
            if (redirectUrl[0] != '/')
                return null;
            // "//host" and "/\host" are protocol-relative: browsers send them cross-origin.
            var isProtocolRelative = redirectUrl.Length > 1 && redirectUrl[1] is '/' or '\\';
            return isProtocolRelative ? null : redirectUrl;
        }

        if (Constants.AppSchemes.All.Contains(uri.Scheme))
            return redirectUrl;
        if (uri.Scheme is "http" or "https" && allowedHosts.Contains(uri.Host))
            return redirectUrl;
        return null;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter FullyQualifiedName~AuthRedirectUrlTest`
Expected: PASS, 16 cases.

- [ ] **Step 6: Wire it into the close flow**

In `src/dotnet/Users.Service/AuthHelper.cs`, replace lines 272-274 inside `IsCloseFlow`:

```csharp
        string? redirectUrl = null;
        if (request.Query.TryGetValue("redirectUrl", out var returnUrlValues))
            redirectUrl = returnUrlValues.FirstOrDefault().NullIfEmpty();
```

with:

```csharp
        string? redirectUrl = null;
        if (request.Query.TryGetValue("redirectUrl", out var returnUrlValues))
            redirectUrl = AuthRedirectUrl.Sanitize(returnUrlValues.FirstOrDefault(), HostInfo.GetHosts());
```

`HostInfo` is already a property on `AuthHelper` (`AuthHelper.cs:15`); `NullIfEmpty()` is redundant because `Sanitize` returns `null` for empty input.

- [ ] **Step 7: Build and run the full unit-test project**

Run: `dotnet build ActualChat.CI.slnf && dotnet test tests/Users.UnitTests/Users.UnitTests.csproj`
Expected: build succeeds, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Constants.AppSchemes.cs src/dotnet/Api/AuthRedirectUrl.cs \
        src/dotnet/Users.Service/AuthHelper.cs tests/Users.UnitTests/AuthRedirectUrlTest.cs
git commit -m "fix(auth): allowlist close-flow redirect URLs"
```

---

### Task 2: `NativeAuth_SignOut` command

Gives MAUI a way to sign out without a browser. Mirrors `OnSignInGoogle` / `OnSignInApple` on the same service.

**Files:**
- Modify: `src/dotnet/Api.Contracts/Users/INativeAuth.cs`
- Modify: `src/dotnet/Users.Service/NativeAuth.cs`
- Modify: `src/dotnet/Api.Contracts/Module/ApiContractsAotSource.g.cs` (regenerated, not hand-edited)
- Test: `tests/Users.IntegrationTests/NativeAuthSignOutTest.cs`

**Interfaces:**
- Consumes: `AccountsBackend_SignOut(Session Session, bool Deactivate = false)` (`src/dotnet/Users.Contracts/IAccountsBackend.cs:68`).
- Produces: `ActualChat.Users.NativeAuth_SignOut(Session Session)` : `ISessionCommand<Unit>, IApiCommand` — Task 5 calls it from `MauiAccountUI`.

- [ ] **Step 1: Write the failing test**

Create `tests/Users.IntegrationTests/NativeAuthSignOutTest.cs`. Modelled on `LegacyNativeAuthControllerTest.cs`, which uses the same fixture, the same Apple token mock, and the same `ConfirmPendingRegistration` dance for first-time sign-in:

```csharp
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class NativeAuthSignOutTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private AppleTokenEndpointHandlerMock AppleTokenHandler { get; }
        = fixture.AppHost.Services.GetRequiredService<AppleTokenEndpointHandlerMock>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();

    [Fact]
    public async Task SignOutShouldMakeSessionGuest()
    {
        // arrange: sign the session in
        var ct = CancellationToken.None;
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session), ct);
        var appleUserId = UniqueNames.AppleId();
        var email = UniqueNames.Email("native-signout", "gmail.com");
        var code = AppleTokenHandler.Setup(appleUserId, email);
        await Commander.Call(new NativeAuth_SignInApple(session, appleUserId, code, email, "Test User"), ct);
        await AppHost.ConfirmPendingRegistration(session);

        var cAccount = await Computed.Capture(() => Accounts.GetOwn(session, ct), ct);
        cAccount = await cAccount
            .When(x => !x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        cAccount.Value.IsGuest.Should().BeFalse();

        // act
        await Commander.Call(new NativeAuth_SignOut(session), ct);

        // assert
        cAccount = await cAccount
            .When(x => x.IsGuestOrNull(), ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        cAccount.Value.IsGuestOrNull().Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter FullyQualifiedName~NativeAuthSignOutTest`
Expected: build failure — `NativeAuth_SignOut` does not exist.

- [ ] **Step 3: Add the command contract**

In `src/dotnet/Api.Contracts/Users/INativeAuth.cs`, add to the interface:

```csharp
    [CommandHandler]
    Task OnSignOut(NativeAuth_SignOut command, CancellationToken cancellationToken);
```

and after `NativeAuth_SignInApple`:

```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignOut(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
```

- [ ] **Step 4: Implement the handler**

In `src/dotnet/Users.Service/NativeAuth.cs`, add after `OnSignInApple` and before the `// Private methods` separator:

```csharp
    // [CommandHandler]
    public virtual async Task OnSignOut(NativeAuth_SignOut command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var signOutCommand = new AccountsBackend_SignOut(session);
        await ((Task)Commander.Call(signOutCommand, true, cancellationToken)).ConfigureAwait(false);
    }
```

The `(Task)` cast matches `AuthHelper.cs:180`, which calls the same command. Unlike the sign-in handlers this one does not swallow exceptions: sign-out has no `SessionTemporals` error channel and the caller should see a failure.

- [ ] **Step 5: Regenerate the AOT helpers**

`ApiContractsAotSource.g.cs` is generated and lists every serializable API command. A new command that is missing from it fails at runtime under AOT (Android release, iOS).

Run: `./update-aot-helpers.cmd`
Expected: `src/dotnet/Api.Contracts/Module/ApiContractsAotSource.g.cs` now contains `NativeAuth_SignOut` entries. Verify with:

```bash
grep -c "NativeAuth_SignOut" src/dotnet/Api.Contracts/Module/ApiContractsAotSource.g.cs
```
Expected: a non-zero count (the sign-in commands each appear ~6 times).

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter FullyQualifiedName~NativeAuthSignOutTest`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api.Contracts src/dotnet/Users.Service/NativeAuth.cs \
        tests/Users.IntegrationTests/NativeAuthSignOutTest.cs
git commit -m "feat(auth): add NativeAuth_SignOut command"
```

---

### Task 3: `MauiWebAuthenticator` for iOS, macOS and Android

**Files:**
- Create: `src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs`
- Create: `src/dotnet/App.Maui/Platforms/Android/WebAuthCallbackActivity.cs`
- Modify: `src/dotnet/Maui/MauiSettings.cs:25`
- Modify: `src/dotnet/App.Maui/Module/MauiAppModule.cs:38`
- Modify: `src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist:64-68`

**Interfaces:**
- Consumes: `Constants.AppSchemes.Prod` / `.Dev` (Task 1); `Microsoft.Maui.Authentication.WebAuthenticator` (already a global using — `App.Maui/GlobalUsings.cs:11`, no new package reference).
- Produces:
  - `MauiSettings.AppScheme` : `const string` (existing name, now sourced from `Constants.AppSchemes`).
  - `MauiSettings.AuthCallbackUrl` : `const string` = `$"{AppScheme}://auth-complete"`.
  - `ActualChat.App.Maui.Services.MauiWebAuthenticator.Run(string url, CancellationToken cancellationToken = default)` → `Task<bool>`; `true` = flow completed, `false` = user cancelled or it timed out. Task 4 adds the Windows branch; Task 5 calls it.

- [ ] **Step 1: Add the callback URL to `MauiSettings`**

In `src/dotnet/Maui/MauiSettings.cs`, replace line 25:

```csharp
    public const string AppScheme = IsDevApp ? "voxt-dev" : "voxt";
```

with:

```csharp
    public const string AppScheme = IsDevApp ? Constants.AppSchemes.Dev : Constants.AppSchemes.Prod;
    // "auth-complete" is the URI host, not a path — the Android intent filter matches on DataHost.
    public const string AuthCallbackUrl = $"{AppScheme}://auth-complete";
```

Leave the `WebAuth` nested class alone for now — Task 5 deletes it together with its last consumers.

- [ ] **Step 2: Write `MauiWebAuthenticator`**

Create `src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs`:

```csharp
namespace ActualChat.App.Maui.Services;

/// <summary>
/// Runs a web authentication flow in the platform's in-app browser
/// (<c>ASWebAuthenticationSession</c> on Apple platforms, Chrome Custom Tabs on Android),
/// returning once the server redirects to <see cref="MauiSettings.AuthCallbackUrl"/>.
/// </summary>
public sealed class MauiWebAuthenticator(IServiceProvider services)
{
    private ILogger Log { get; } = services.LogFor<MauiWebAuthenticator>();

    public async Task<bool> Run(string url, CancellationToken cancellationToken = default)
    {
        try {
            var options = new WebAuthenticatorOptions {
                Url = url.ToUri(),
                CallbackUrl = MauiSettings.AuthCallbackUrl.ToUri(),
                // Apple-only; keeps the flow out of the shared Safari cookie jar,
                // which also removes the system consent alert.
                PrefersEphemeralWebBrowserSession = true,
            };
            await WebAuthenticator.Default.AuthenticateAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException) {
            Log.LogInformation("Web auth flow was canceled");
            return false;
        }
        catch (Exception e) {
            Log.LogError(e, "Web auth flow failed");
            return false;
        }
    }
}
```

`url.ToUri()` is the codebase's own string extension (used in `MauiSettings.cs:44` and `AppDelegate.cs:136`).

- [ ] **Step 3: Register it in DI**

In `src/dotnet/App.Maui/Module/MauiAppModule.cs`, under the `// Session & authentication` comment at line 37, after the `MauiSession` registration:

```csharp
        services.AddSingleton(c => new MauiWebAuthenticator(c));
```

- [ ] **Step 4: Add the Android callback activity**

MAUI's Android `WebAuthenticator` needs an activity registered for the callback scheme, otherwise `AuthenticateAsync` throws at runtime.

Create `src/dotnet/App.Maui/Platforms/Android/WebAuthCallbackActivity.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Content.PM;

namespace ActualChat.App.Maui;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = MauiSettings.AppScheme,
    DataHost = "auth-complete")]
public class WebAuthCallbackActivity : WebAuthenticatorCallbackActivity;
```

`MauiSettings.AppScheme` is a `const`, so it is legal in an attribute argument and the dev/prod APKs get `voxt-dev` / `voxt` respectively.

- [ ] **Step 5: Fix the Mac Catalyst scheme collision**

`Platforms/MacCatalyst/Info.plist` registers **both** schemes with no CI rewrite, so the dev app also claims `voxt://`. Once callbacks are live, macOS could deliver a prod sign-in callback to the dev app and hang the prod app's sheet.

Edit `src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist` lines 64-68, from:

```xml
			<key>CFBundleURLSchemes</key>
			<array>
				<string>voxt-dev</string>
				<string>voxt</string>
			</array>
```

to (matching `Platforms/iOS/Info.plist:79-83`, which CI rewrites for prod):

```xml
			<key>CFBundleURLSchemes</key>
			<array>
                <!-- Replaced with voxt for prod instance -->
				<string>voxt-dev</string>
			</array>
```

Task 6 adds the matching CI rewrite step.

- [ ] **Step 6: Build the Android target**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build succeeds. This is the only MAUI TFM buildable in this environment; the iOS/macOS code in this task is compile-verified by CI.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Maui/MauiSettings.cs \
        src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs \
        src/dotnet/App.Maui/Module/MauiAppModule.cs \
        src/dotnet/App.Maui/Platforms/Android/WebAuthCallbackActivity.cs \
        src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist
git commit -m "feat(auth): add MauiWebAuthenticator for Apple platforms and Android"
```

---

### Task 4: Windows protocol registration and activation bridge

Windows has no in-app browser and the app is unpackaged (`App.Maui.csproj:814` sets `WindowsPackageType=None`), so MAUI's `WebAuthenticator` is not usable. This task builds the equivalent from the pieces already present: launch the default browser, then wait for a protocol activation delivered through the existing single-instance redirection.

**Files:**
- Create: `src/dotnet/App.Maui/Platforms/Windows/WindowsAppScheme.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Windows/App.Activation.cs:32-38`
- Modify: `src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs`

**Interfaces:**
- Consumes: `ActualChat.App.Maui.WinUI.App.AppInstanceActivated` : `event Action<string>` (`App.Activation.cs:8`), and its existing `AppInstance.FindOrRegisterForKey` / `RedirectActivationToAsync` single-instance logic (`App.Activation.cs:20-30`), which already routes an activation of a second launch into the running instance.
- Produces: `WindowsAppScheme.EnsureRegistered()` → `void`, idempotent.

- [ ] **Step 1: Forward protocol activations**

`OnAppInstanceActivated` currently casts `e.Data` to `LaunchActivatedEventArgs` and returns early for anything else — so a `voxt://` activation is silently dropped today.

In `src/dotnet/App.Maui/Platforms/Windows/App.Activation.cs`, replace lines 32-38:

```csharp
    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        var e2 = e.Data as Windows.ApplicationModel.Activation.LaunchActivatedEventArgs;
        if (e2 == null)
            return;
        AppInstanceActivated.Invoke(e2.Arguments);
    }
```

with:

```csharp
    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        var arguments = e.Data switch {
            Windows.ApplicationModel.Activation.LaunchActivatedEventArgs x => x.Arguments,
            Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs x => x.Uri.AbsoluteUri,
            _ => null,
        };
        if (arguments != null)
            AppInstanceActivated.Invoke(arguments);
    }
```

- [ ] **Step 2: Add the registry registration**

Create `src/dotnet/App.Maui/Platforms/Windows/WindowsAppScheme.cs`:

```csharp
using Microsoft.Win32;

namespace ActualChat.App.Maui;

/// <summary>
/// Registers the app's custom URL scheme under HKCU. Packaged apps get this from
/// their manifest; this app is unpackaged (WindowsPackageType=None), so it must
/// register itself — and re-register whenever the executable moves.
/// </summary>
public static class WindowsAppScheme
{
    public static void EnsureRegistered()
    {
        var exePath = Environment.ProcessPath;
        if (exePath.IsNullOrEmpty())
            return;

        var command = $"\"{exePath}\" \"%1\"";
        using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MauiSettings.AppScheme}");
        schemeKey.SetValue(null, $"URL:{MauiSettings.AppScheme}");
        schemeKey.SetValue("URL Protocol", "");
        using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
        if (!Equals(commandKey.GetValue(null), command))
            commandKey.SetValue(null, command);
    }
}
```

- [ ] **Step 3: Add the Windows branch to `MauiWebAuthenticator`**

`MauiWebAuthenticator.Run` from Task 3 becomes platform-split. Replace the body of `src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs` with:

```csharp
namespace ActualChat.App.Maui.Services;

/// <summary>
/// Runs a web authentication flow in the platform's in-app browser
/// (<c>ASWebAuthenticationSession</c> on Apple platforms, Chrome Custom Tabs on Android),
/// returning once the server redirects to <see cref="MauiSettings.AuthCallbackUrl"/>.
/// Windows has no in-app browser, so it falls back to the default browser plus
/// protocol activation.
/// </summary>
public sealed class MauiWebAuthenticator(IServiceProvider services)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private ILogger Log { get; } = services.LogFor<MauiWebAuthenticator>();

    public async Task<bool> Run(string url, CancellationToken cancellationToken = default)
    {
        try {
#if WINDOWS
            return await RunWindows(url, cancellationToken).ConfigureAwait(false);
#else
            var options = new WebAuthenticatorOptions {
                Url = url.ToUri(),
                CallbackUrl = MauiSettings.AuthCallbackUrl.ToUri(),
                // Apple-only; keeps the flow out of the shared Safari cookie jar,
                // which also removes the system consent alert.
                PrefersEphemeralWebBrowserSession = true,
            };
            await WebAuthenticator.Default.AuthenticateAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
#endif
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or TimeoutException) {
            Log.LogInformation("Web auth flow was canceled or timed out");
            return false;
        }
        catch (Exception e) {
            Log.LogError(e, "Web auth flow failed");
            return false;
        }
    }

#if WINDOWS
    // Private methods

    private async Task<bool> RunWindows(string url, CancellationToken cancellationToken)
    {
        WindowsAppScheme.EnsureRegistered();
        var callbackSource = TaskCompletionSourceExt.New<bool>();
        void OnActivated(string arguments) {
            if (arguments.StartsWith(MauiSettings.AuthCallbackUrl, StringComparison.OrdinalIgnoreCase))
                callbackSource.TrySetResult(true);
        }

        WinUI.App.AppInstanceActivated += OnActivated;
        try {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.External).ConfigureAwait(false);
            return await callbackSource.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
        }
        finally {
            WinUI.App.AppInstanceActivated -= OnActivated;
        }
    }
#endif
}
```

`TaskCompletionSourceExt.New<T>()` is ActualLab's helper, already used across this codebase; if the analyzer objects to the `WinUI` namespace shorthand, qualify it as `ActualChat.App.Maui.WinUI.App`.

- [ ] **Step 4: Verify the non-Windows build still compiles**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build succeeds — the `#if WINDOWS` block is excluded, and the shared path is unchanged from Task 3.

The Windows TFM cannot be built in this environment (`App.Maui.csproj:12` gates it on Windows). It is compile-verified by CI.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Windows/WindowsAppScheme.cs \
        src/dotnet/App.Maui/Platforms/Windows/App.Activation.cs \
        src/dotnet/App.Maui/Services/MauiWebAuthenticator.cs
git commit -m "feat(auth): add Windows protocol activation path for web auth"
```

---

### Task 5: Switch `MauiAccountUI` over and delete the dead browser code

**Files:**
- Modify: `src/dotnet/App.Maui/Services/MauiAccountUI.cs:18-86`
- Modify: `src/dotnet/Maui/MauiSettings.cs:63-66`
- Modify: `src/dotnet/App.Maui/WebView/MauiWebView.Navigation.cs:10-13,86-126`

**Interfaces:**
- Consumes: `MauiWebAuthenticator.Run(string, CancellationToken)` → `Task<bool>` (Tasks 3-4); `MauiSettings.AuthCallbackUrl` (Task 3); `NativeAuth_SignOut(Session)` (Task 2).
- Produces: nothing downstream — this is the last code task.

- [ ] **Step 1: Rewrite `MauiAccountUI`**

Replace the body of `src/dotnet/App.Maui/Services/MauiAccountUI.cs` from line 18 (`protected override async Task SignInBackend`) to the end of the file with:

```csharp
    protected override async Task SignInBackend(string schema)
    {
        if (schema.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(schema));

#if ANDROID
        if (schema == AuthSchema.Google) {
            var googleAuth = Hub.Services.GetRequiredService<NativeGoogleAuth>();
            if (googleAuth.IsAvailable()) {
                await googleAuth.SignIn().ConfigureAwait(false);
                return;
            }
        }
#endif
#if IOS
        if (schema == AuthSchema.Apple
            && DeviceInfo.Platform == DevicePlatform.iOS
            && DeviceInfo.Version.Major >= 13)
        {
            var appleAuth = Hub.Services.GetRequiredService<NativeAppleAuth>();
            await appleAuth.SignIn().ConfigureAwait(false);
            return;
        }
#endif

        await WebSignIn($"/signIn/{schema}").ConfigureAwait(false);
    }

    protected override async Task SignOutBackend()
    {
#if ANDROID
        var googleAuth = Hub.Services.GetRequiredService<NativeGoogleAuth>();
        if (googleAuth.IsSignedIn())
            await googleAuth.SignOut().ConfigureAwait(true);
#endif

        await Hub.Services.Commander().Call(new NativeAuth_SignOut(Session)).ConfigureAwait(false);
    }

    // Private methods

    private async Task WebSignIn(string endpoint)
    {
        try {
            var sessionToken = await Hub.SessionTokens.Get(TimeSpan.FromMinutes(15)).ConfigureAwait(true);
            var url = $"{MauiSettings.BaseUrl}maui-auth/start"
                + $"?s={sessionToken.Token.UrlEncode()}"
                + $"&e={endpoint.UrlEncode()}"
                + $"&flow={"Sign-in".UrlEncode()}"
                + $"&appKind={HostInfo.AppKind:G}"
                + $"&redirectUrl={MauiSettings.AuthCallbackUrl.UrlEncode()}";
            var webAuthenticator = Hub.Services.GetRequiredService<MauiWebAuthenticator>();
            await webAuthenticator.Run(url).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "WebSignIn failed (endpoint: {Endpoint})", endpoint);
        }
    }
}
```

Notes on what changed and why:
- The old `redirectUrl` pointed at `Links.Chats` / `Links.Home` on the host, relying on `MauiNavigationInterceptor` to bounce it back to the local URL. That indirection is gone: the redirect now targets the app scheme directly, which is what dismisses the browser sheet.
- Sign-out no longer touches the web, so the shared `WebSignInOrSignOut` helper collapses into a sign-in-only `WebSignIn` and the `isSignIn` branching disappears.
- `Nav` and `UrlMapper` are no longer used here; remove any `using` directives that go unused (the analyzer will flag them).

- [ ] **Step 2: Delete `MauiSettings.WebAuth`**

Its last consumers are gone. In `src/dotnet/Maui/MauiSettings.cs`, delete lines 63-66:

```csharp
    public static class WebAuth
    {
        public static readonly bool UseSystemBrowser = true;
    }
```

Leave the `// Nested types` separator and the `Diagnostics` class.

- [ ] **Step 3: Delete the dead auth routing in the webview**

The app's own webview never hosts auth now. In `src/dotnet/App.Maui/WebView/MauiWebView.Navigation.cs`:

Replace lines 10-13:

```csharp
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly HashSet<string> AllowedExternalHosts = MauiSettings.WebAuth.UseSystemBrowser
        ? new() { "www.youtube.com" }
        : new() { "accounts.google.com", "appleid.apple.com" };
```

with:

```csharp
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly HashSet<string> AllowedExternalHosts = new() { "www.youtube.com" };
```

Then delete the whole `IsAllowedHostUri` branch — lines 87-100, from the `// If we're here, it's a host URL` comment through the closing brace of the `if (IsAllowedHostUri(uri))` block — leaving:

```csharp
        // If we're here, it's a host URL, so we have to re-route it to the local one
        var localUri = HostToAbsoluteLocalUri(uri);
```

and delete the now-unreferenced `IsAllowedHostUri` method at lines 111-126. Its own call-site comment already says "We never land here, coz IsAllowedHostUri(...) always returns false now".

- [ ] **Step 4: Build the Android target**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build succeeds with no unused-using or unreachable-code warnings.

- [ ] **Step 5: Verify nothing still references the removed members**

Run:
```bash
grep -rn "UseSystemBrowser\|IsAllowedHostUri\|WebSignInOrSignOut" --include=*.cs src/
```
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/App.Maui/Services/MauiAccountUI.cs \
        src/dotnet/Maui/MauiSettings.cs \
        src/dotnet/App.Maui/WebView/MauiWebView.Navigation.cs
git commit -m "feat(auth): sign in via the in-app browser, sign out without one"
```

---

### Task 6: CI plist rewrite for the Mac Catalyst prod flavor

Task 3 removed `voxt` from the Mac Catalyst plist so the dev app stops claiming the prod scheme. The prod build now needs the same rewrite iOS already gets, or prod macOS callbacks will not reach the app at all.

**Files:**
- Modify: `.github/workflows/build-test-deploy-dev.yml`

**Interfaces:**
- Consumes: the existing `IS_DEV_MAUI` workflow env var and the `dppeak/update-ios-plist-action@v1.1.0` action already used at line 684.
- Produces: nothing downstream.

- [ ] **Step 1: Locate the insertion point**

Mac Catalyst is built by its own job, `build-maccatalyst-pkg` (line 762) — *not* the iOS job, so the existing iOS plist step at line 684 does not cover it. Within that job the order is:

```
line 867   - name: Prepare GoogleServices file for PROD
line 872     EOF
line 874   # publish-maccatalyst (Release) builds, signs with the Apple Distribution cert,
line 877   - name: Build app package
```

The new step goes between line 872 and the comment at line 874.

Run: `grep -n "Prepare GoogleServices file for PROD" .github/workflows/build-test-deploy-dev.yml`
Expected: two hits — one in the iOS job (~line 680), one in the Mac Catalyst job (~line 867). Use the **second**.

- [ ] **Step 2: Add the rewrite step**

```yaml
      - name: PList - set MacCatalyst URL scheme to voxt for prod
        if: ${{ env.IS_DEV_MAUI == 'false' }}
        uses: dppeak/update-ios-plist-action@v1.1.0
        with:
          info-plist-path: "src/dotnet/App.Maui/Platforms/MacCatalyst/Info.plist"
          key-value-json: '[{"CFBundleURLTypes": [{"CFBundleTypeRole": "Editor", "CFBundleURLName": "ai.voxt", "CFBundleURLSchemes": ["voxt"]}]}]'
          print-file: true
```

This is byte-for-byte the iOS step at line 684 with the plist path swapped. Note that `build-maccatalyst-pkg` already defines `IS_DEV_MAUI` in its `env` block (line 771), so the `if:` condition works unchanged.

- [ ] **Step 3: Validate the workflow YAML**

Run: `pwsh -c "gh workflow view build-test-deploy-dev.yml --repo (git remote get-url origin)"` — or, offline: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/build-test-deploy-dev.yml')); print('ok')"`
Expected: `ok` / no parse error.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/build-test-deploy-dev.yml
git commit -m "ci(auth): rewrite MacCatalyst URL scheme for the prod flavor"
```

---

## Manual verification

None of the platform behavior is unit-testable; this matrix is the real acceptance gate and must be run before the PR merges.

**Per platform** — iOS, Android, Mac Catalyst, Windows:

| Case | Expected |
|---|---|
| Google sign-in | Sheet opens **in-app** (Windows: browser), user signs in, sheet dismisses automatically, app shows signed-in state |
| Apple sign-in | iOS uses the native sheet; other platforms use the in-app browser and dismiss automatically |
| User dismisses the sheet | Returns to the sign-in screen, no error toast, app remains usable |
| Server-side error (e.g. revoke the OAuth client secret) | Returns to the app, `ProviderSelectStep` shows the error message |
| Sign-out | Completes with **no browser or sheet at all**; account becomes guest |
| Sign in again after sign-out | Google shows the account chooser (`prompt=select_account`) |

**Cross-flavor** — install **both** the dev and prod apps on macOS and on Windows, sign in from each, and confirm each callback reaches the app that started the flow. This is the regression the Task 3 plist fix and the Task 4 per-flavor registry key exist to prevent.

**Host override** — in the dev app, point `MauiAppServerInstanceSelector` at a worktree host, then sign in with Google on Android. `NativeGoogleAuth.IsAvailable()` returns `false` under an override (`NativeGoogleAuth.cs:47`), so this must fall through to Custom Tabs rather than the external browser.

## Notes for the reviewer

- **Nothing changes in the Google or Apple consoles.** The OAuth callback still lands on the server; redirect URIs stay `https://voxt.ai/signin-google` and `https://dev.voxt.ai/signin-google`. The custom scheme is used only for the final server→app hop.
- **Task 1 is a security fix in its own right** and is deliberately first: `RootServerPage.razor:431` is an open redirect today, and Task 5 widens that parameter to custom schemes.
- **Task 2's AOT regeneration step is load-bearing.** A command missing from `ApiContractsAotSource.g.cs` fails only at runtime, only under AOT (Android release, iOS) — it will not show up in any local build.
