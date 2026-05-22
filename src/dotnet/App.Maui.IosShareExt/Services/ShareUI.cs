using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Contacts;
using ActualChat.Maui;
using ActualChat.Search;
using ActualChat.UI.App.Services;
using ActualChat.UI.Services;
using ActualLab.Fusion.UI;
using ActualLab.Generators;
using ActualLab.Interception;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class ShareUI : WorkerBase, IComputeService, INotifyInitialized
{
    private readonly HashSet<ContactId> _selectedIds = new();
    private readonly MutableState<bool> _canSend;
    private readonly MutableState<double> _uploadPct;
    private readonly FuncWorker _sendWorker;
    private MemSearchQuery _searchQuery;
    private readonly MutableState<bool> _isInitialized;
    private readonly MutableState<bool> _isSending;
    private readonly MutableState<bool> _isSent;
    private readonly MutableState<string> _failureMessage;
    private bool _hasFiles;

    public MutableState<PlaceId?> SelectedPlaceId { get; }
    public IState<double> UploadPct => _uploadPct;
    public IState<bool> CanSend => _canSend;
    public IState<string> FailureMessage => _failureMessage;

    private IosHub Hub { get; }
    private IAccounts Accounts => Hub.Accounts;
    private IContacts Contacts => Hub.Contacts;
    private ShareInputs SharedInputs => Hub.SharedData;
    private ChunkedFileUploader FileUploader => Hub.FileUploader;
    private VideoTranscoder VideoTranscoder => Hub.VideoTranscoder;
    private Session Session => Hub.Session;
    private UICommander UICommander => Hub.UICommander;
    private ICommander Commander => Hub.Commander;
    private IncomingShareSuggestions ShareSuggestions => field ??= Hub.Services.GetRequiredService<IncomingShareSuggestions>();
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public ShareUI(IosHub hub)
    {
        Hub = hub;
        SelectedPlaceId = Hub.StateFactory.NewMutable<PlaceId?>();
        Hub.Services.GetRequiredService<ChunkSizeSelectorRecommendation>().Multiplier = 1;
        _isInitialized = Hub.StateFactory.NewMutable<bool>();
        _isSending = Hub.StateFactory.NewMutable<bool>();
        _isSent = Hub.StateFactory.NewMutable<bool>();
        _failureMessage = Hub.StateFactory.NewMutable<string>("");
        _uploadPct = Hub.StateFactory.NewMutable<double>();
        _canSend = Hub.StateFactory.NewMutable<bool>();
        _sendWorker = FuncWorker.New(ct
            => AsyncChain.From(SendInternal)
                .LogError(Log)
                .RunIsolated(ct));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
            if (ownAccount.IsGuest)
                return;

            _hasFiles = await SharedInputs.HasFiles().ConfigureAwait(false);
            if (await UIKitExt.GetSuggestedRecipient().ConfigureAwait(false) is not { } chatId)
                return;

            // Auto-send only if the user can actually send to the suggested chat,
            // otherwise fall through to manual contact selection.
            var contactId = ContactId.NewAny(ownAccount.Id, chatId);
            var contact = await Contacts.Get(Session, contactId, cancellationToken).ConfigureAwait(false);
            if (contact is null || !CanSendTo(contact))
                return;

            _selectedIds.Add(contactId);
            _canSend.Value = true;
            StartSending();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize");
            _failureMessage.Value = e.UserFriendlyMessage;
        }
        finally {
            _isInitialized.Value = true;
        }
    }

    private bool CanSendTo(Contact contact)
    {
        var rules = contact.Chat.Rules;
        return rules.CanWrite() && (!_hasFiles || rules.CanUpload());
    }

    protected override Task DisposeAsyncCore()
        => _sendWorker.DisposeSilentlyAsync().AsTask();

    [ComputeMethod]
    public virtual async Task<ShareStep> GetStep(CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.IsGuest)
            return ShareStep.SignIn;

        var failureMessage = await _failureMessage.Use(cancellationToken).ConfigureAwait(false);
        if (!failureMessage.IsNullOrEmpty())
            return ShareStep.Failed;

        var isSent = await _isSent.Use(cancellationToken).ConfigureAwait(false);
        if (isSent)
            return ShareStep.Completed;

        var isSending = await _isSending.Use(cancellationToken).ConfigureAwait(false);
        if (isSending)
            return ShareStep.Uploading;

        var isInitialized = await _isInitialized.Use(cancellationToken).ConfigureAwait(false);
        if (!isInitialized)
            return ShareStep.None;

        return ShareStep.ContactSelection;
    }

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

        var sendable = contacts
            .SkipNullItems()
            .Where(CanSendTo);

        if (_searchQuery.IsEmpty)
            return sendable.ToList();

        return sendable
            .WithSearchMatchRank(_searchQuery, c => new MemSearchDocument(c.Chat.Title))
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
        _searchQuery = new MemSearchQuery(filter);

        using (Invalidation.Begin())
            _ = ListContacts(default);
    }

    public void StartSending()
    {
        _isSending.Value = true;
        _sendWorker.Start(true);
    }

    public async Task OpenMainApp(CancellationToken cancellationToken)
    {
        var url = new NSUrl($"{MauiSettings.AppScheme}://");
        Log.LogInformation("Opening main app `{Url}`", url);
        await UIKitExt.OpenUrl(url, cancellationToken).ConfigureAwait(false);
        await UIKitExt.CloseApp(cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelUploading(CancellationToken cancellationToken)
    {
        await _sendWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        await UIKitExt.CloseApp(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendInternal(CancellationToken cancellationToken)
    {
        try
        {
            var chatIds = _selectedIds.Select(x => x.ChatId).Distinct().ToList();
            SuggestShareContacts([.._selectedIds]);
            var text = await SharedInputs.GetText(cancellationToken).ConfigureAwait(false);
            var fileInputs = await SharedInputs.ListFiles(cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Text: {Text}, Files: {Files}", text.ToPrivate(), fileInputs.Count);
            if (text.IsNullOrWhiteSpace() && fileInputs.Count == 0) {
                Log.LogError("No text or files available for upload");
                return;
            }

            var progress = new ForkableProgress(pct => _uploadPct.Value = pct);
            var (uploadProgress, entryProgress) = progress.Fork(0.95, 0.05);
            var mediaRefs = await UploadFiles(fileInputs, uploadProgress, cancellationToken)
                .ConfigureAwait(false);
            var attachments = mediaRefs
                .Select(r => new ChatEntryAttachment { MediaId = r.MediaId, ThumbnailMediaId = r.ThumbnailMediaId })
                .ToArray();

            var chatProgresses = entryProgress.Fork(chatIds.Count);
            for (var i = 0; i < chatIds.Count; i++) {
                var chatId = chatIds[i];
                var entryText = text;
                foreach (var attachmentList in attachments.Chunk(10)) {
                    await CreateChatEntry(chatId, entryText, attachmentList, cancellationToken).ConfigureAwait(false);
                    entryText = "";
                }
                if (!entryText.IsNullOrWhiteSpace())
                    await CreateChatEntry(chatId, entryText, [], cancellationToken).ConfigureAwait(false);
                chatProgresses[i].Report(100);
            }

            _isSent.Value = true;

            UIKitExt.PlaySuccessHaptic();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            await UIKitExt.CloseApp(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(cancellationToken)) {
                Log.LogError(e, "Failed to send message");
                _failureMessage.Value = e.UserFriendlyMessage;
            }
            throw;
        }
    }

    private void SuggestShareContacts(IReadOnlyList<ContactId> contactIds)
    {
        foreach (var contactId in contactIds)
            ShareSuggestions.Push(contactId);
    }

    private async Task CreateChatEntry(
        ChatId chatId,
        string text,
        ChatEntryAttachment[] attachments,
        CancellationToken cancellationToken)
    {
        var cmd = new Chats_UpsertEntry(Session, chatId, null) {
            Text = text,
            ClientId = RandomStringGenerator.Default.Next(6),
            Attachments = attachments,
        };
        await UICommander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MediaRef[]> UploadFiles(
        IReadOnlyList<NSItemProvider> fileInputs,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (fileInputs.Count == 0) {
            progress.Report(100);
            return [];
        }
        var mediaRefs = new MediaRef[fileInputs.Count];
        var fileForks = progress.Fork(fileInputs.Count);
        for (var i = 0; i < fileInputs.Count; i++) {
            var fileInput = fileInputs[i];
            mediaRefs[i] = await UploadFile(fileInput, fileForks[i], cancellationToken).ConfigureAwait(false);
        }
        return mediaRefs;
    }

    private async Task<MediaRef> UploadFile(
        NSItemProvider fileInput,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        // Split progress: transcoding 20%, upload 80%
        var (transcodingProgress, uploadProgress) = progress.Fork(0.2, 0.8);
        using var dUploadSource = await PrepareUploadSource(fileInput, transcodingProgress, cancellationToken).ConfigureAwait(false);
        var uploadSource = dUploadSource.Resource;

        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), uploadSource.Metadata.FileName.Value)
            .Set(nameof(Media.Media.ContentType), uploadSource.Metadata.ContentType);
        var uploadId = await InitUpload().ConfigureAwait(false);
        var streamSource = (StreamUploadSource)uploadSource.StreamSource;
        await FileUploader.UploadData(uploadId, streamSource.GetStream(), uploadProgress, cancellationToken).ConfigureAwait(false);
        return await CompleteUpload().ConfigureAwait(false);

        Task<UploadId> InitUpload()
        {
            var cmd = new Uploads_Create(Session, uploadSource.Metadata.Length, "", metadata);
            return UICommander.Call(cmd, cancellationToken);
        }

        Task<MediaRef> CompleteUpload()
            => Commander.Call(new Uploads_ConvertToMediaRef(Session, uploadId), CancellationToken.None);
    }

    private async Task<Disposable<UploadSource>> PrepareUploadSource(
        NSItemProvider fileInput,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var uploadSource = await fileInput.ToUploadSource().ConfigureAwait(false);
        if (uploadSource.StreamSource is not FileUploadSource fileSource) {
            progress.Report(100); // Mark transcoding phase as complete
            return Disposable.New(uploadSource, Delegates<UploadSource>.Noop);
        }

        var transcodedFilePath = await VideoTranscoder
            .Transcode(fileSource.FilePath, uploadSource.Metadata.ContentType, progress, cancellationToken)
            .ConfigureAwait(false);
        if (transcodedFilePath.IsEmpty) {
            progress.Report(100); // Mark transcoding phase as complete
            return Disposable.New(uploadSource, Delegates<UploadSource>.Noop);
        }

        // Use transcoded file's extension but keep original name stem
        var newMetadata = new UploadSourceMetadata(
            MediaMimeTypes.GetMimeType(transcodedFilePath),
            new FileInfo(transcodedFilePath).Length,
            uploadSource.Metadata.FileName.ChangeExtension(transcodedFilePath.Extension));
        return Disposable.New(new UploadSource(newMetadata, new FileUploadSource(transcodedFilePath)),
            _ => File.Delete(transcodedFilePath));
    }
}
