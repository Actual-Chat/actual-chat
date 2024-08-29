using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

internal sealed class WebAuth(UIHub hub) : IClientAuth
{
    private readonly string _jsClassName = $"{BlazorUICoreModule.ImportName}.WebAuth";
    private (string Schema, string DisplayName)[]? _cachedSchemas;

    private UIHub Hub { get; } = hub;
    private IJSRuntime JS => Hub.JSRuntime();

    public (string Name, string DisplayName)[] GetSchemas()
        => _cachedSchemas ??= AuthSchema.ToSchemasWithDisplayNames(AuthSchema.AllExternal);

    public Task SignIn(string schema)
        => JS.InvokeVoidAsync($"{_jsClassName}.signIn", schema).AsTask();

    public Task SignOut()
        => JS.InvokeVoidAsync($"{_jsClassName}.signOut").AsTask();
}
