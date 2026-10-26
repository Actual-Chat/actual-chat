Add the next code to your app project:

```xml
<ItemGroup>
    <ProjectReference Include="..\App.Maui.IosShareExt\App.Maui.IosShareExt.csproj">
        <IsAppExtension>true</IsAppExtension>
        <IsWatchApp>false</IsWatchApp>
    </ProjectReference>
</ItemGroup>
```

## What the extension shares with the app

The extension is a separate process with its own bundle id, so it sees neither the
app's `localStorage` nor its `NSUserDefaults`. Two entitlements bridge that:

- **Keychain access group** (`M287G8G83F.chat.actual[.dev].app.shared`) — the session,
  see `AppleSharedSecureStorage`.
- **App Group** (`group.chat.actual[.dev].app.shared`) — the theme, the UI
  language and the RPC endpoint, see `MauiPreferences.Theme`, `MauiPreferences.UILanguage`
  and `MauiPreferences.RpcEndpoint`. `MauiPreferences` caches what it reads for the life of the
  process, and the extension's process outlives many shares, so `ShareViewController` drops those
  cached values at the start of each share (`MauiPreferences.InvalidateShared`).
  - The app writes the theme on every theme change (`MauiThemeHandler.OnThemeChanged`);
    `ShareViewController` reads it per share and applies it to `AppColors` and
    `OverrideUserInterfaceStyle`. Nothing written means "follow the system
    appearance", which is what the extension did before.
  - The app writes the language it renders in on every launch
    (`MauiBrowserInfo.OnInitialized`); `AppStrings.L` reads it and resolves the
    catalog against it. Nothing written means "follow `NSLocale.PreferredLanguages`",
    which is what a device that hasn't run the app since #4261 gets.
  - The app writes the RPC endpoint its `RpcEndpointMonitor` picks (`MauiRpcEndpointSelector`);
    the extension moves to the app's choice at the start of each share (`RpcEndpointFollower`,
    a worker of the share's scope). Nothing written means the origin.
    That choice may come from another network, so the extension then checks it once with
    `RpcPathProbe` and, if it fails, moves to the first candidate that passes - and, through
    the same selector, writes that back for the app's next start. It keeps no path history and
    gets no relay attestation, so through a relay the origin sees the relay's IP. Content
    requests follow the endpoint through `ContentHostHandler`, which `MauiModule` adds to every
    `HttpClient`.

Both entitlements have to be granted by the provisioning profile the build is signed
with, otherwise codesign fails with `MT7140`. Adding one means registering it on the
Apple Developer portal, enabling it for every member id, and regenerating each of their
profiles — the Debug ones plus `App Store [Share] [Dev]`. The App Group's members are
`chat.actual[.dev].app`, `chat.actual[.dev].app.share`, `chat.actual[.dev].app.widget` and
`chat.actual[.dev].app.notification`; the notification service extension reads the RPC endpoint
(see `src/swift/VoxtNotificationService/README.md`), and the widget is provisioned but reads
nothing yet, see `src/swift/VoxtActivities/README.md`.
