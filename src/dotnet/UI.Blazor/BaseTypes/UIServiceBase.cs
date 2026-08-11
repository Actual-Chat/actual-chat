using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualLab.Diagnostics;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor;

public abstract class UIServiceBase<THub>(THub hub) : IHasDisposeStatus
    where THub : UIHub
{
    public THub Hub { get; } = hub;

    // Service shortcuts copied from CircuitHubComponentBase
    protected IServiceProvider Services => Hub.Services;
    protected Session Session => Hub.Session;
    protected StateFactory StateFactory => Hub.StateFactory;
    protected ICommander Commander => Hub.Commander;
    protected UICommander UICommander => Hub.UICommander;
    protected NavigationManager Nav => Hub.Nav;
    protected IJSRuntime JS => Hub.JS;

    // Core UI service shortcuts
    protected HostInfo HostInfo => Hub.HostInfo;
    protected UrlMapper UrlMapper => Hub.UrlMapper;
    protected MomentClockSet Clocks => field ??= Hub.Clocks;
    protected DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    protected Temporals Temporals => Hub.Temporals;
    protected LocalSettings LocalSettings => Hub.LocalSettings;
    protected UserSettingsUI UserSettingsUI => Hub.UserSettingsUI;
    protected ComponentIdGenerator ComponentIdGenerator => Hub.ComponentIdGenerator;
    protected DiffEngine DiffEngine => Hub.DiffEngine;
    protected History History => Hub.History;
    protected UIEventHub UIEventHub => Hub.UIEventHub;
    protected AccountUI AccountUI => Hub.AccountUI;
    protected PanelsUI PanelsUI => Hub.PanelsUI;
    protected ModalUI ModalUI => Hub.ModalUI;
    protected ToastUI ToastUI => Hub.ToastUI;
    protected TuneUI TuneUI => Hub.TuneUI;
    protected ShareUI ShareUI => Hub.ShareUI;
    protected Dispatcher Dispatcher => Hub.Dispatcher;
    protected Features Features => Hub.Features;
    protected IStringLocalizer L => Hub.StringLocalizer;

    // Shortcuts
    public bool IsDisposed => Hub.IsDisposed;
    protected bool IsPrerendering => Hub.IsPrerendering;
    protected bool IsInteractive => Hub.IsInteractive;

    protected ILogger Log => field ??= Hub.LoggerFactory.CreateLogger(GetType().NonProxyType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);
    protected Tracer Tracer => field ??= Hub.Tracer[GetType()];
}
