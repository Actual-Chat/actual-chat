namespace ActualChat.UI.Blazor.Services;

public class WebClientAuthHelper(IServiceProvider services) : ClientAuthHelper(services)
{
    public override async ValueTask<(string Name, string DisplayName)[]> GetSchemas()
    {
        if (CachedSchemas != null)
            return CachedSchemas;

        var sSchemas = await JSRuntime
            .InvokeAsync<string>("window.App.getAuthSchemas")
            .ConfigureAwait(false); // The rest of this method doesn't depend on Blazor
        var lSchemas = ListFormat.Default.Parse(sSchemas);
        var schemas = new (string, string)[lSchemas.Count / 2];
        for (int i = 0, j = 0; i < schemas.Length; i++, j += 2)
            schemas[i] = (lSchemas[j], lSchemas[j + 1]);
        CachedSchemas = schemas;
        return CachedSchemas;
    }
}
