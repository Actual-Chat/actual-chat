using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Media;
using ActualChat.Search;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.UI;
using ActualLab.Generators;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class ShareUI : UIServiceBase, IComputeService
{
    private readonly HashSet<ContactId> _selectedIds = new();
    private readonly MutableState<bool> _canSend;
    private readonly MutableState<double> _uploadPct;
    private readonly FuncWorker _sendWorker;
    private SearchPhrase _searchPhrase = SearchPhrase.None;
    private readonly MutableState<bool> _isUploading;
    private readonly MutableState<bool> _isFailed;

    public MutableState<PlaceId?> SelectedPlaceId { get; }
    public IState<bool> IsUploading => _isUploading;
    public IState<bool> IsFailed => _isFailed;
    public IState<double> UploadPct => _uploadPct;
    public IState<bool> CanSend => _canSend;

    private IContacts Contacts => Hub.Contacts;
    private ShareInputs SharedInputs => Hub.SharedData;
    private ChunkedFileUploader FileUploader => Hub.FileUploader;
    private Session Session => Hub.Session;
    private UICommander UICommander => Hub.UICommander;
    private ICommander Commander => Hub.Commander;

    public ShareUI(IosHub hub) : base(hub)
    {
        SelectedPlaceId = Hub.StateFactory.NewMutable<PlaceId?>();
        _isUploading = Hub.StateFactory.NewMutable<bool>();
        _isFailed = Hub.StateFactory.NewMutable<bool>();
        _uploadPct = Hub.StateFactory.NewMutable<double>();
        _canSend = Hub.StateFactory.NewMutable<bool>();
        _sendWorker = FuncWorker.New(ct
            => AsyncChain.From(SendInternal)
                .LogError(Log)
                .RunIsolated(ct));
    }

    protected override Task DisposeAsyncCore()
        => _sendWorker.DisposeSilentlyAsync().AsTask();

    [ComputeMethod]
    public virtual Task<bool> IsContactSelected(ContactId contactId, CancellationToken cancellationToken)
        => Task.FromResult(_selectedIds.Contains(contactId));

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Contact>> ListContacts(CancellationToken cancellationToken)
    {
        var placeId = await SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds.Select(x => Contacts.Get(Session, x, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        if (_searchPhrase.IsEmpty)
            return contacts.SkipNullItems().ToList();

        return contacts
            .SkipNullItems()
            .WithSearchMatchRank(_searchPhrase, c => c.Chat.Title)
            .FilterBySearchMatchRank()
            .OrderBySearchMatchRank()
            .WithoutSearchMatchRank()
            .ToList();
    }

    public void ToggleSelection(ContactId contactId)
    {
        if (!_selectedIds.Add(contactId))
            _selectedIds.Remove(contactId);
        _canSend.Value = _selectedIds.Count > 0;

        using (Invalidation.Begin())
            _ = IsContactSelected(contactId, default);
    }

    public void SetFilter(string filter)
    {
        Log.LogInformation("Set filter: {Filter}", filter);
        _searchPhrase = filter.ToSearchPhrase(true, false);

        using (Invalidation.Begin())
            _ = ListContacts(default);
    }

    public Task CloseApp(CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(() => UIKitExt.ExtensionContext.CompleteRequestAsync([]));

    public void StartSending()
    {
        _isUploading.Value = true;
        _sendWorker.Start(true);
    }

    public async Task CancelUploading(CancellationToken cancellationToken)
    {
        await _sendWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        await CloseApp(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendInternal(CancellationToken cancellationToken)
    {
        try
        {
            var chatIds = _selectedIds.Select(x => x.ChatId).Distinct().ToList();
            var text = await SharedInputs.GetText(cancellationToken).ConfigureAwait(false);
            var fileInputs = await SharedInputs.ListFiles(cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Text: {Text}, Files: {Files}", text, fileInputs.Count);
            if (text.IsNullOrWhiteSpace() && fileInputs.Count == 0) {
                Log.LogError("No text or files available for upload");
                return;
            }

            // TODO: max 10 attachments per message

            Log.LogInformation("Uploading to chats: {ChatIds}", string.Join(",", chatIds));
            // TODO(FC): single upload for all chats !!!!!!!!!!!!!!!!!!!!!!!!!!!
            for (var i = 0; i < chatIds.Count; i++) {
                // TODO: handle error
                // TODO: handle cancellation
                var totalPct = i * 100 / chatIds.Count;
                var chatId = chatIds[i];
                var progress = new Progress<double>(pct => _uploadPct.Value = totalPct + (pct / chatIds.Count));
                var attachments = await UploadFiles(chatId, fileInputs, progress, cancellationToken)
                    .ConfigureAwait(false);
                var cmd = new Chats_UpsertTextEntry(Session, chatId, null, text) {
                    ClientId = RandomStringGenerator.Default.Next(6),
                    EntryAttachments = attachments,
                };
                await UICommander.Call(cmd, cancellationToken).ConfigureAwait(false);
            }
            await CloseApp(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _isFailed.Value = true;
            throw;
        }
    }

    private async Task<TextEntryAttachment[]> UploadFiles(
        ChatId chatId,
        IReadOnlyList<NSItemProvider> uploadInputs,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var attachments = new TextEntryAttachment[uploadInputs.Count];
        for (var i = 0; i < uploadInputs.Count; i++) {
            var totalPct = i * 100 / uploadInputs.Count;
            var uploadInput = uploadInputs[i];
            var fileProgress = new Progress<double>(pct => progress.Report(totalPct + (pct / uploadInputs.Count)));
            attachments[i] = await UploadFile(chatId, uploadInput, fileProgress, cancellationToken).ConfigureAwait(false);
        }
        return attachments;
    }

    private async Task<TextEntryAttachment> UploadFile(
        ChatId chatId,
        NSItemProvider fileInput,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var uploadInput = await fileInput.ToUploadInput().ConfigureAwait(false);
        await using var _1 = uploadInput.ConfigureAwait(false);
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), uploadInput.FileName)
            .Set(nameof(Media.Media.ContentType), uploadInput.ContentType);
        var uploadId = await InitUpload().ConfigureAwait(false);
        await FileUploader.UploadData(uploadId, Task.FromResult(uploadInput.Stream), progress, cancellationToken).ConfigureAwait(false);
        var mediaContent = await CompleteUpload().ConfigureAwait(false);
        return new TextEntryAttachment {
            MediaId = mediaContent.MediaId,
            ThumbnailMediaId = mediaContent.ThumbnailMediaId,
        };

        Task<UploadId> InitUpload()
        {
            var cmd = new Uploads_Create(Session, uploadInput.Stream.Length, UploadExt.BuildTag(chatId), metadata);
            return UICommander.Call(cmd, cancellationToken);
        }

        Task<MediaContent> CompleteUpload()
            => Commander.Call(new Uploads_ConvertToMediaContent(Session, uploadId), CancellationToken.None);
    }
}
