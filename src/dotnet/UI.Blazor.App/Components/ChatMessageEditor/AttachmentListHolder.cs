using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentListHolder : UIServiceBase<AppUIHub>
{
    private readonly ChatId _chatId;
    private AttachmentList _attachments;

    private UploadSessions UploadSessions => Hub.UploadSessions;

    public event EventHandler? Changed;

    public AttachmentList Attachments => _attachments;

    public AttachmentListHolder(AppUIHub hub, ChatId chatId) : base(hub)
    {
        _chatId = chatId;
        _attachments = CreateAttachmentList();
        SubscribeToListEvents(_attachments);
    }

    public ResetIntent PopSnapshot() {
        Dispatcher.AssertAccess();
        UnsubscribeFromListEvents(_attachments);
        var attachments = _attachments;
        _attachments = CreateAttachmentList();
        SubscribeToListEvents(_attachments);
        RaiseChanged();
        return new (attachments, () => Rollback(attachments), () => Release(attachments));
    }

    private AttachmentList CreateAttachmentList()
        => new (UploadSessions);

    private ValueTask Release(AttachmentList attachments)
    {
        Dispatcher.AssertAccess();
        return attachments.DisposeSilentlyAsync();
    }

    private async ValueTask Rollback(AttachmentList attachments) {
        Dispatcher.AssertAccess();
        AttachmentList? backup = null;
        if (_attachments.Count == 0) {
            backup = _attachments;
            _attachments = attachments;
        }
        if (backup != null) {
            UnsubscribeFromListEvents(backup);
            await backup.DisposeSilentlyAsync();
            SubscribeToListEvents(attachments);
        }
        RaiseChanged();
    }

    private void SubscribeToListEvents(AttachmentList attachments)
        => attachments.Changed += OnAttachmentListChanged;

    private void UnsubscribeFromListEvents(AttachmentList attachments)
        => attachments.Changed -= OnAttachmentListChanged;

    private void OnAttachmentListChanged(object? sender, EventArgs e)
        => RaiseChanged();

    private void RaiseChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

    public async Task SetSentAttachments(string[] urls) {
        throw new NotImplementedException();
        // var downloader = Hub.Services.GetRequiredService<IIncomingShareFileDownloader>();
        // var fileNames = urls
        //     .Select(c => {
        //         downloader.TryExtractFileName(c, out var fileName);
        //         return fileName;
        //     })
        //     .ToArray();
        // await JSRef.InvokeAsync<int>("addBlobs", [urls, fileNames]).ConfigureAwait(false);
    }

    // Nested types

    public sealed class ResetIntent : IAsyncDisposable {
        private readonly Func<ValueTask> _confirm;
        private readonly Func<ValueTask> _rollback;
        private bool _isRolledBack;
        public AttachmentList Attachments { get; }

        public ValueTask DisposeAsync()
            => !_isRolledBack ? _confirm.Invoke() : default;

        internal ResetIntent(AttachmentList attachments, Func<ValueTask> rollback, Func<ValueTask> confirm) {
            Attachments = attachments;
            _rollback = rollback;
            _confirm = confirm;
        }

        public async ValueTask Rollback() {
            await _rollback();
            _isRolledBack = true;
        }
    }
}
