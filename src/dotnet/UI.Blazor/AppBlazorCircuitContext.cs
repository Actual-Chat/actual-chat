using ActualLab.Internal;

namespace ActualChat.UI.Blazor;

public sealed class AppBlazorCircuitContext : BlazorCircuitContext, IDispatcherResolver
{
    public ComponentBase RootComponent {
        get => field ?? throw Errors.NotInitialized();
        private set;
    } = null!;

    public AppBlazorCircuitContext(IServiceProvider services)
        : base(services)
    {
        if (!OSInfo.IsWebAssembly)
            Log.LogInformation("[+] #{Id}", Id.Format());
    }

    protected override Task DisposeAsyncCore()
    {
        if (!OSInfo.IsWebAssembly)
            Log.LogInformation("[-] #{Id}", Id.Format());
        return Task.CompletedTask;
    }

    public override void Initialize(
        Dispatcher dispatcher,
        RenderModeDef renderMode)
        => throw new NotSupportedException("Use another implementation of Initialize.");

    public void Initialize(ComponentBase rootComponent, RenderModeDef renderMode)
    {
        var dispatcher = rootComponent.GetDispatcher();
        lock (WhenInitializedSource) {
            if (WhenInitializedSource.Task.IsCompleted) {
                if (Dispatcher == dispatcher && RenderMode == renderMode) {
                    RootComponent = rootComponent;
                    return;
                }

                throw Errors.AlreadyInitialized();
            }

            RootComponent = rootComponent;
            Dispatcher = dispatcher;
            RenderMode = renderMode;
            WhenInitializedSource.TrySetResult(default);
        }
    }
}
