using ActualChat.Localization;
using ActualChat.UI.Blazor;
using ActualLab.IO;
using ActualChat.UI.Blazor.Services;
using AppKit;

namespace ActualChat.App.Maui;

/// <summary>
/// Desktop-idiomatic <see cref="IFileSaver"/>: NSSavePanel for a single file, a folder picker
/// for a group, streaming each download straight to the chosen destination. Windows registers
/// no saver and lets WebView2 handle the JS &lt;a download&gt; fallback; WKWebView has no download
/// pipeline - it navigates the main frame to the blob: URL instead - so AppKit needs a native one.
/// </summary>
public sealed class MacOSFileSaver(UIHub hub) : UIServiceBase<UIHub>(hub), IFileSaver
{
    private HttpClient HttpClient
        => field ??= Hub.Services.HttpClientFactory().CreateClient(GetType().Name);

    public async Task Save(IReadOnlyList<FileToSave> files)
    {
        if (files.Count == 0)
            return;

        try {
            var destinations = await PickDestinations(files).ConfigureAwait(false);
            if (destinations.Count == 0)
                return; // The user cancelled the panel

            foreach (var (file, destination) in destinations)
                await DownloadTo(file, destination).ConfigureAwait(false);
            ToastUI.Show(L.FileSaver_Saved(destinations.Count, destinations.Count),
                "icon-checkmark-circle-2", ToastDismissDelay.Short);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to save files");
            UICommander.ShowError(e);
        }
    }

    // Private methods

    private Task<List<(FileToSave File, string Destination)>> PickDestinations(IReadOnlyList<FileToSave> files)
        => DispatchToMainThread(() => {
            var destinations = new List<(FileToSave, string)>();
            if (files.Count == 1) {
                var file = files[0];
                var panel = new NSSavePanel {
                    NameFieldStringValue = GetFileName(file.FileName, file.ContentType),
                    CanCreateDirectories = true,
                };
                if (panel.RunModal() == 1 && panel.Url?.Path is { } path)
                    destinations.Add((file, path));
            }
            else {
                var panel = new NSOpenPanel {
                    CanChooseFiles = false,
                    CanChooseDirectories = true,
                    CanCreateDirectories = true,
                    AllowsMultipleSelection = false,
                    Prompt = L.Common_Save,
                };
                if (panel.RunModal() == 1 && panel.Url?.Path is { } directory)
                    foreach (var file in files)
                        destinations.Add((file,
                            ((FilePath)directory & GetFileName(file.FileName, file.ContentType)).ToUniqueNumbered()));
            }
            return destinations;
        });

    private async Task DownloadTo(FileToSave file, string destination)
    {
        var response = await HttpClient.GetAsync(file.Url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var fileStream = File.Create(destination);
        await using var __ = fileStream.ConfigureAwait(false);
        await stream.CopyToAsync(fileStream).ConfigureAwait(false);
    }

    private static string GetFileName(string fileName, string contentType)
    {
        if (!fileName.IsNullOrEmpty())
            return fileName;

        var extension = MediaTypeExt.GetFileExtension(contentType)
            ?? throw StandardError.Constraint("Not supported media type.");
        return "download" + extension;
    }
}
