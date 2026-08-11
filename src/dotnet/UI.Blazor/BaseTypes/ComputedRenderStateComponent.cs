using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor;

public abstract class ComputedRenderStateComponent<THub, TState> : ComputedRenderStateComponent<TState>
    where THub : UIHub
{
    private THub? _hub;

    protected THub Hub => _hub!;

    // Core UI service shortcuts
    protected HostInfo HostInfo => Hub.HostInfo;
    protected UrlMapper UrlMapper => Hub.UrlMapper;
    protected MomentClockSet Clocks => Hub.Clocks;
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
    protected bool IsPrerendering => Hub.IsPrerendering;
    protected bool IsInteractive => Hub.IsInteractive;

    protected ComputedRenderStateComponent()
        => Options = DefaultOptions | ComputedStateComponentOptions.ComputeStateOnThreadPool; // Prevent blocking the UI thread

    public override Task SetParametersAsync(ParameterView parameters)
    {
        _hub ??= (THub)CircuitHub;
        return base.SetParametersAsync(parameters);
    }
}
