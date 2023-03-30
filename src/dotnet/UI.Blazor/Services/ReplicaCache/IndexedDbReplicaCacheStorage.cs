using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class IndexedDbReplicaCacheStorage : IReplicaCacheStorage
{
    private JSRuntimeAccessor JsAccessor { get; }

    public IndexedDbReplicaCacheStorage(
        // We use IJSRuntime accessor because IJSRuntime is registered as scoped service in mobile apps,
        // but storage is resolved from root container.
        JSRuntimeAccessor jsAccessor)
        => JsAccessor = jsAccessor;

    public async Task<string?> TryGetValue(string key)
        => await JsAccessor.JS.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.get",
            key).ConfigureAwait(false);

    public async Task SetValue(string key, string value)
        => await JsAccessor.JS.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.set",
            key, value).ConfigureAwait(false);

    public async Task Clear()
        => await JsAccessor.JS.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.clear")
            .ConfigureAwait(false);

    public class JSRuntimeAccessor
    {
        private readonly Func<IJSRuntime> _accessor;

        public IJSRuntime JS
            => _accessor();

        public JSRuntimeAccessor(Func<IJSRuntime> accessor)
            => _accessor = accessor;
    }
}
