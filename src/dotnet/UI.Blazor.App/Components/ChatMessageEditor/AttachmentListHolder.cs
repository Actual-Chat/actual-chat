using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class AttachmentListHolder : UIServiceBase<AppUIHub>
{
    private AttachmentList _attachments;

    public event EventHandler? Changed;

    public AttachmentList Attachments => _attachments;

    public string MediaScope { get; init; } = "";

    public AttachmentListHolder(AppUIHub hub) : base(hub)
    {
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
    {
        var list = new AttachmentList { MediaScope = MediaScope };
        var attachmentsController = Services.GetRequiredService<AttachmentsController>();
        list.Subscribe(attachmentsController);
        return list;
    }

    private ValueTask Rollback(AttachmentList attachments) {
        Dispatcher.AssertAccess();
        AttachmentList? backup = null;
        if (_attachments.Count == 0) {
            backup = _attachments;
            _attachments = attachments;
        }
        if (backup != null) {
            UnsubscribeFromListEvents(backup);
            SubscribeToListEvents(attachments);
        }
        RaiseChanged();
        return ValueTask.CompletedTask;
    }

    private async ValueTask Release(AttachmentList attachments)
    {
        foreach (var attachment in attachments.Items) {
            foreach (var cleanup in attachment.Cleanups.Items) {
                try {
                    await cleanup.Cleanup().ConfigureAwait(false);
                }
                catch (Exception e) {
                    Log.LogWarning(e, "Failed to cleanup attachment {AttachmentId}", attachment.Id);
                }
            }
        }
    }

    private void SubscribeToListEvents(AttachmentList attachments)
        => attachments.Changed += OnAttachmentListChanged;

    private void UnsubscribeFromListEvents(AttachmentList attachments)
        => attachments.Changed -= OnAttachmentListChanged;

    private void OnAttachmentListChanged(object? sender, EventArgs e)
        => RaiseChanged();

    private void RaiseChanged()
        => Changed?.Invoke(this, EventArgs.Empty);

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
