namespace ActualChat.UI.Blazor;

public sealed class AppBlazorCircuitContext : BlazorCircuitContext, IDispatcherResolver
{
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
}
