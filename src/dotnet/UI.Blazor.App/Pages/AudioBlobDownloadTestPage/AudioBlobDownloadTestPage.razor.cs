namespace ActualChat.UI.Blazor.App.Pages;

public partial class AudioBlobDownloadTestPage : ComponentBase
{
    [Inject] private IChats Chats { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;
    [Inject] private UrlMapper UrlMapper { get; init; } = null!;
    [Inject] private ILogger<AudioBlobDownloadTestPage> Log { get; init; } = null!;

    private readonly FormModel _form = new();
    private string _status = "";
    private bool _isBusy;
    private ResolvedEntryInfo? _resolved;
    private string _downloadHref = "";
    private string _downloadFileName = "";

    private async Task OnResolveClick()
    {
        if (_isBusy)
            return;

        _isBusy = true;
        _status = "";
        _resolved = null;
        _downloadHref = "";
        _downloadFileName = "";
        try {
            if (!TryParseInput(_form.Input, UrlMapper, out var chatId, out var lid, out var parseError)) {
                _status = parseError;
                return;
            }

            var entryId = ChatEntryId.New(chatId, lid);
            var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(true);
            if (chat is null) {
                _status = "Chat not found or you have no access";
                return;
            }
            var entry = await Chats.GetEntry(Session, entryId, CancellationToken.None).ConfigureAwait(true);
            if (entry is null) {
                _status = "Chat entry not found";
                return;
            }

            var audio = entry.Audio;
            _resolved = new ResolvedEntryInfo(
                ChatId: chatId.Value,
                LocalId: lid,
                EntryId: entryId.Value,
                BlobId: audio?.BlobId ?? "",
                MediaId: audio?.MediaId?.Value ?? "",
                BeginsAt: audio?.BeginsAt.ToString() ?? "",
                EndsAt: audio?.EndsAt?.ToString() ?? "",
                Duration: audio?.Duration?.ToString("F3", null) ?? "",
                IsStreaming: audio?.IsStreaming ?? false);

            if (audio is null || audio.BlobId.IsNullOrEmpty()) {
                _status = "Entry has no audio blob.";
                return;
            }

            _downloadHref = UrlMapper.ToAbsolute($"/api/audio-blob/{chatId.Value}/{lid.Format()}");
            var extension = Path.GetExtension(audio.BlobId);
            if (extension.IsNullOrEmpty())
                extension = ".webm";
            _downloadFileName = $"{chatId.Value}_{lid.Format()}{extension}";
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to resolve audio blob for input: '{Input}'", _form.Input);
            _status = $"Error: {e.Message}";
        }
        finally {
            _isBusy = false;
        }
    }

    private static bool TryParseInput(
        string? input,
        UrlMapper urlMapper,
        out ChatId chatId,
        out long lid,
        out string error)
    {
        chatId = default!;
        lid = 0;
        if (input.IsNullOrEmpty()) {
            error = "Empty input.";
            return false;
        }

        var trimmed = input.Trim();
        LocalUrl localUrl;
        if (UrlMapper.IsAbsolute(trimmed)) {
            if (LocalUrl.FromAbsolute(trimmed, urlMapper) is not { } fromAbsolute) {
                error = $"URL doesn't belong to the current host '{urlMapper.BaseUri.Host}'.";
                return false;
            }
            localUrl = fromAbsolute;
        }
        else {
            localUrl = trimmed;
        }

        if (!localUrl.IsChat(out var parsedChatId, out long parsedLid)) {
            error = "Couldn't parse a chat URL — expected '/chat/<chatId>?n=<lid>'.";
            return false;
        }
        if (parsedLid <= 0) {
            error = "URL is missing the entry's local id ('?n=<lid>').";
            return false;
        }

        chatId = parsedChatId;
        lid = parsedLid;
        error = "";
        return true;
    }

    private sealed record ResolvedEntryInfo(
        string ChatId,
        long LocalId,
        string EntryId,
        string BlobId,
        string MediaId,
        string BeginsAt,
        string EndsAt,
        string Duration,
        bool IsStreaming);

    private sealed class FormModel
    {
        public string Input { get; set; } = "";
    }
}
