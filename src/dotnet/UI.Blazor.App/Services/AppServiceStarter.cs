using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppServiceStarter
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(AppServiceStarter)];

    private IServiceProvider Services { get; }

    public AppServiceStarter(IServiceProvider services)
        => Services = services;

    public async Task PreWebViewWarmup(CancellationToken cancellationToken)
    {
        using var _1 = _tracer.Region(nameof(PreWebViewWarmup));
        try {
            await Task.Run(() => {
                WarmupSerializer(new Account());
                WarmupSerializer(new AccountFull());
                WarmupSerializer(new Contacts.Contact());
                WarmupSerializer(new Chat.Chat());
                WarmupSerializer(new ChatTile());
                WarmupSerializer(new Mention());
                WarmupSerializer(new Reaction());
                WarmupSerializer(new Author());
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            _tracer.Point($"{nameof(PreWebViewWarmup)} failed, error: " + e);
        }
    }

    public async Task PostSessionSetupWarmup(CancellationToken cancellationToken)
    {
        using var _1 = _tracer.Region(nameof(PostSessionSetupWarmup));
        try {
            await Task.Run(() => {
                _ = Services.GetRequiredService<AccountUI>();
                var chatListUI = Services.GetRequiredService<ChatListUI>();
                return Task.WhenAll(
                    chatListUI.ListActive(cancellationToken),
                    chatListUI.ListAllUnordered(cancellationToken)
                );
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            _tracer.Point($"{nameof(PostSessionSetupWarmup)} failed, error: " + e);
        }
    }

    public async Task ReadyToRender(CancellationToken cancellationToken)
    {
 #pragma warning disable MA0004
        using var _1 = _tracer.Region(nameof(ReadyToRender));

        // Starting BrowserInfo
        _tracer.Point("BrowserInfo.Initialize");
        var browserInfo = Services.GetRequiredService<BrowserInfo>();
        _ = browserInfo.Initialize();

        // Starting Audio pipeline, load Workers and connect to Audio channel
        _tracer.Point("AudioInfo.Initialize");
        var audioInfo = Services.GetRequiredService<AudioInfo>();
        _ = audioInfo.Initialize();

        // Starting ThemeUI
        _tracer.Point("ThemeUI.Start");
        var themeUI = Services.GetRequiredService<ThemeUI>();
        themeUI.Start();

        await browserInfo.WhenReady.WaitAsync(cancellationToken);

        var timeZoneConverter = Services.GetRequiredService<TimeZoneConverter>();
        if (timeZoneConverter is ServerSideTimeZoneConverter serverSideTimeZoneConverter)
            serverSideTimeZoneConverter.Initialize(browserInfo.UtcOffset);
        _tracer.Point("BrowserInfo is ready");

        // Finishing w/ theme
        await themeUI.WhenReady.WaitAsync(cancellationToken);
        _tracer.Point("ThemeUI is ready");

        // Initialize History
        var history = Services.GetRequiredService<History>();
        await history.Initialize();
        _tracer.Point("History is ready");

        var uiLifetimeEvents = Services.GetRequiredService<UILifetimeEvents>();
        uiLifetimeEvents.RaiseOnAppInitialized(Services);
 #pragma warning restore MA0004
    }

    public Task AfterFirstRender(CancellationToken cancellationToken)
    {
        // Starting less important UI services
        Services.GetRequiredService<AppPresenceReporter>().Start();
        return Task.CompletedTask;
    }

    // Private methods

    private void WarmupSerializer<T>(T value)
    {
        var s = TextSerializer.Default;
        var text = "";

        // Write
        try {
            text = s.Write(value);
        }
        catch {
            // Intended
        }

        // Read
        try {
            _ = s.Read<T>(text);
        }
        catch {
            // Intended
        }
    }

    private void WarmupReplicaService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();

    private void WarmupComputeService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();
}
