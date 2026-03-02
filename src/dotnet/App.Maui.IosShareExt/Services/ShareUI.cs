using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Maui;
using ActualChat.Media;
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
    private SearchPhrase _searchPhrase = SearchPhrase.None;
    private readonly MutableState<bool> _isSending;
    private readonly MutableState<bool> _isSent;
    private readonly MutableState<bool> _hasFailed;

    public MutableState<PlaceId?> SelectedPlaceId { get; }
    public IState<double> UploadPct => _uploadPct;
    public IState<bool> CanSend => _canSend;

    private IosHub Hub { get; }
    private IAccounts Accounts => Hub.Accounts;
    private IContacts Contacts => Hub.Contacts;
    private ShareInputs SharedInputs => Hub.SharedData;
    private ChunkedFileUploader FileUploader => Hub.FileUploader;
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
        _isSending = Hub.StateFactory.NewMutable<bool>();
        _isSent = Hub.StateFactory.NewMutable<bool>();
        _hasFailed = Hub.StateFactory.NewMutable<bool>();
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

            if (await UIKitExt.GetSuggestedRecipient().ConfigureAwait(false) is not { } chatId)
                return;

            // Auto-send when sharing from a contact suggestion
            var contactId = ContactId.NewAny(ownAccount.Id, chatId);
            _selectedIds.Add(contactId);
            _canSend.Value = true;
            StartSending();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize");
            _hasFailed.Value = true;
        }
    }

    protected override Task DisposeAsyncCore()
        => _sendWorker.DisposeSilentlyAsync().AsTask();

    [ComputeMethod]
    public virtual async Task<ShareStep> GetStep(CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.IsGuest)
            return ShareStep.SignIn;

        var hasFailed = await _hasFailed.Use(cancellationToken).ConfigureAwait(false);
        if (hasFailed)
            return ShareStep.Failed;

        var isSent = await _isSent.Use(cancellationToken).ConfigureAwait(false);
        if (isSent)
            return ShareStep.Completed;

        var isSending = await _isSending.Use(cancellationToken).ConfigureAwait(false);
        if (isSending)
            return ShareStep.Uploading;

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

            // TODO: max 10 attachments per message

            Log.LogInformation("Uploading to chats: {ChatIds}", string.Join(",", chatIds));
            // TODO(FC): single upload for all chats !!!!!!!!!!!!!!!!!!!!!!!!!!!
            for (var i = 0; i < chatIds.Count; i++) {
                // TODO: handle cancellation
                var totalPct = i * 100 / chatIds.Count;
                var chatId = chatIds[i];
                var progress = new Progress<double>(pct => _uploadPct.Value = totalPct + (pct / chatIds.Count));
                var attachments = await UploadFiles(chatId, fileInputs, progress, cancellationToken)
                    .ConfigureAwait(false);
                var entryText = text;
                foreach (var attachmentList in attachments.Chunk(10)) {
                    await CreateChatEntry(chatId, entryText, attachmentList, cancellationToken).ConfigureAwait(false);
                    entryText = "";
                }
                if (!entryText.IsNullOrWhiteSpace())
                    await CreateChatEntry(chatId, entryText, [], cancellationToken).ConfigureAwait(false);
            }

            _isSent.Value = true;

            UIKitExt.PlaySuccessHaptic();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            await UIKitExt.CloseApp(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Failed to send message");
            _hasFailed.Value = true;
            throw;
        }
    }

    private void SuggestShareContacts(IReadOnlyList<ContactId> contactIds)
    {
        foreach (var contactId in contactIds)
            ShareSuggestions.Push(contactId);
    }

    private async Task CreateChatEntry(ChatId chatId, string entryText, TextEntryAttachment[] attachmentList, CancellationToken cancellationToken)
    {
        var cmd = new Chats_UpsertTextEntry(Session, chatId, null) {
            Text = entryText,
            ClientId = RandomStringGenerator.Default.Next(6),
            EntryAttachments = attachmentList,
        };
        await UICommander.Call(cmd, cancellationToken).ConfigureAwait(false);
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
        using var uploadInput = await fileInput.ToUploadInput().ConfigureAwait(false);
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), uploadInput.FileName)
            .Set(nameof(Media.Media.ContentType), uploadInput.ContentType);
        var uploadId = await InitUpload().ConfigureAwait(false);
        await FileUploader.UploadData(uploadId, Task.FromResult(uploadInput.Stream.Resource), progress, cancellationToken).ConfigureAwait(false);
        var mediaContent = await CompleteUpload().ConfigureAwait(false);
        return new TextEntryAttachment {
            MediaId = mediaContent.MediaId,
            ThumbnailMediaId = mediaContent.ThumbnailMediaId,
        };

        Task<UploadId> InitUpload()
        {
            var cmd = new Uploads_Create(Session, uploadInput.Stream.Resource.Length, UploadExt.BuildTag(chatId), metadata);
            return UICommander.Call(cmd, cancellationToken);
        }

        Task<MediaContent> CompleteUpload()
            => Commander.Call(new Uploads_ConvertToMediaContent(Session, uploadId), CancellationToken.None);
    }
}
