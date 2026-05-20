using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception.Trimming;

namespace ActualChat.UI.Blazor.Module;

// Manual CodeKeeper entries — not overwritten by the generator.
internal partial class BlazorUIAotSource
{
    static BlazorUIAotSource()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        // Services used via DI/reflection with [JSInvokable] or JS interop
        CodeKeeper.Keep<BrowserInfo>();
        CodeKeeper.Keep<WebShareInfo>();
        CodeKeeper.Keep<History>();
        CodeKeeper.Keep<FontSizeUI>();
        CodeKeeper.Keep<DebugUI>();
        CodeKeeper.Keep<UserActivityUI>();

        // VirtualList data types (components are kept by the generated AotSource)
        CodeKeeper.Keep<VirtualListDataQuery>();
        CodeKeeper.Keep<VirtualListRenderState>();

        // TaskMonitor needs TaskScheduler internals
        CodeKeeper.Keep<TaskScheduler>();

        // From misc. states & ComputedSource-s
        ProxyCodeKeeper.KeepMethodResult<ScreenSize, ScreenSize>();
    }
}
