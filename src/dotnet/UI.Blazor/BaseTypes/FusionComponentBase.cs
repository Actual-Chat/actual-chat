using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor;

public abstract class FusionComponentBase<THub> : FusionComponentBase, IHasCircuitHub
    where THub : UIHub
{
    [Inject] protected THub Hub { get; init; } = null!;

    // Service shortcuts copied from CircuitHubComponentBase
    protected IServiceProvider Services => Hub.Services;
    protected Session Session => Hub.Session;
    protected StateFactory StateFactory => Hub.StateFactory;
    protected UICommander UICommander => Hub.UICommander;
    protected NavigationManager Nav => Hub.Nav;
    protected IJSRuntime JS => Hub.JS;

    // Core UI service shortcuts
    protected HostInfo HostInfo => Hub.HostInfo;
    protected UrlMapper UrlMapper => Hub.UrlMapper;
    protected MomentClockSet Clocks => Hub.Clocks;
    protected DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    protected DateFormatter DateFormatter => Hub.DateFormatter;
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
    protected bool IsPrerendering => Hub.IsPrerendering;
    protected bool IsInteractive => Hub.IsInteractive;
    protected bool ShouldAutoFocusField => Hub.BrowserInfo.ShouldAutoFocusField;

    // Explicit IHasFusionHub & IHasServices implementation
    CircuitHub IHasCircuitHub.CircuitHub => Hub;
    IServiceProvider IHasServices.Services => Services;
}
