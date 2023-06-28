using ActualChat.UI.Blazor.Module;
using Stl.Rpc.Caching;

namespace ActualChat.UI.Blazor.Services;

#pragma warning disable MA0056
#pragma warning disable MA0064

public sealed class IndexedDbClientComputedCache : AppClientComputedCache
{
    private static readonly TextOrBytes? Null = default;
    private static readonly string GetJSName = $"{BlazorUICoreModule.ImportName}.IndexedDb.get";
    private static readonly string SetManyJSName = $"{BlazorUICoreModule.ImportName}.IndexedDb.setMany";
    private static readonly string ClearJSName = $"{BlazorUICoreModule.ImportName}.IndexedDb.clear";

    private IJSRuntime JS { get; }

    public IndexedDbClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services, false)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        // ReSharper disable once VirtualMemberCallInConstructor
        WhenInitialized = Initialize(settings.Version);
    }

    protected override async ValueTask<TextOrBytes?> Fetch(RpcCacheKey key, CancellationToken cancellationToken)
    {
        var value = await JS.InvokeAsync<string?>(GetJSName, cancellationToken, key.ToString());
        return value == null ? Null : new TextOrBytes(Convert.FromBase64String(value));
    }

    public override async Task Clear(CancellationToken cancellationToken = default)
        => await JS.InvokeVoidAsync(ClearJSName, cancellationToken);

    protected override async Task Flush(Dictionary<RpcCacheKey, TextOrBytes?> flushingQueue)
    {
        var keys = new string[flushingQueue.Count];
        var values = new string?[flushingQueue.Count];
        var i = 0;
        foreach (var (key, value) in flushingQueue) {
            keys[i] = key.ToString();
            if (value is { } vValue)
                values[i] = Convert.ToBase64String(vValue.Data.Span);
            else
                values[i] = null;
            i++;
        }
        await JS.InvokeVoidAsync(SetManyJSName, CancellationToken.None, keys, values);
    }
}
