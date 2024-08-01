using System.Diagnostics.CodeAnalysis;
using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class FontSizeUI : ScopedServiceBase<UIHub>
{
    private static readonly string JSFontSizesClassName = "window.FontSizes";
    private static readonly string JSFontSizeListMethod = $"{JSFontSizesClassName}.list";
    private static readonly string JSFontSizeGetMethod = $"{JSFontSizesClassName}.get";
    private static readonly string JSFontSizeSetMethod = $"{JSFontSizesClassName}.set";

    private string[]? _fontSizes = null;

    public ISyncedState<string> FontSize { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FontSizeUI))]
    public FontSizeUI(UIHub hub) : base(hub)
    {
        FontSize = Hub.StateFactory().NewCustomSynced(new SyncedState<string>.CustomOptions(Reader, Writer));
        return;

        Task<string> Reader(CancellationToken cancellationToken)
        {
            var js = Hub.JSRuntime();
            return Hub.Dispatcher.InvokeAsync(()
                => js.InvokeAsync<string>(JSFontSizeGetMethod, cancellationToken).AsTask());
        }

        async Task Writer(string fontSize, CancellationToken cancellationToken)
        {
            var supportedFontSizes = await List(cancellationToken).ConfigureAwait(false);
            if (!supportedFontSizes.Contains(fontSize, StringComparer.OrdinalIgnoreCase))
                throw StandardError.Constraint($"FontSize '{fontSize}' is not supported.");

            var js = Hub.JSRuntime();
            await Hub.Dispatcher.InvokeAsync( ()
                => js.InvokeAsync<string>(JSFontSizeSetMethod, fontSize));
        }
    }

    public async Task<string[]> List(CancellationToken cancellationToken)
    {
        if (_fontSizes != null)
            return _fontSizes;

        var js = Hub.JSRuntime();
        var fontSizeMap = await js.InvokeAsync<Dictionary<string, string>>(JSFontSizeListMethod, cancellationToken);
        return _fontSizes = fontSizeMap
            .Select(m => m.Key)
            .ToArray();
    }

    public int GetFontSizePixels()
        => FontSize.Value switch {
            "14px" => 14,
            "16px" => 16,
            "18px" => 18,
            "20px" => 20,
            _ => 14,
        };
}
