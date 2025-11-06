namespace ActualChat.UI.Blazor.App.Services;

public class MediaUploadsUI
{
    private readonly Lock _chatSendingMessagesLock = new (); // This lock is used add/remove ChatSendingMessages and add/remove items inside.
    private readonly List<(SendingMessage, AttachmentUploads)> _uploads = new ();
    private readonly ChatSendingMessagesTriggers _triggers;

    public MediaUploadsUI(ChatSendingMessagesTriggers triggers)
        => _triggers = triggers;

    public AttachmentUploads? GetMediaUploads(ChatEntry entry)
    {
        if (!entry.IsSending)
            return GetMediaUploads(entry.Id);

        if (!(entry.SendingTag is SendingMessage sendingMessage))
            return null;

        return GetMediaUploads(sendingMessage);
    }

    public void Add(SendingMessage sendingMessage, AttachmentUploads uploads)
    {
        lock (_chatSendingMessagesLock) {
            _uploads.Add((sendingMessage, uploads));
            using (Invalidation.Begin())
                _ = _triggers.OnMediaUploadsChanged(sendingMessage);
        }
    }

    public void Delete(SendingMessage sendingMessage)
    {
        lock (_chatSendingMessagesLock)
            if (_uploads.RemoveAll(c => Equals(c.Item1, sendingMessage)) == 0)
                return;

        Invalidate(sendingMessage);
    }

    public void Invalidate(SendingMessage sendingMessage)
    {
        using (Invalidation.Begin()) {
            if (sendingMessage.PostedChatEntry is {} chatEntry)
                _ = _triggers.OnMediaUploadsChanged(chatEntry.Id);
            _ = _triggers.OnMediaUploadsChanged(sendingMessage);
        }
    }

    public void NotifyAttachmentsLoaded(ChatEntryId chatEntryId)
    {
        lock (_chatSendingMessagesLock) {
            var uploadsItem = _uploads.FirstOrDefault(c => Equals(c.Item1.PostedChatEntry?.Id, chatEntryId));
            if (uploadsItem.Item1 is null || !uploadsItem.Item2.WhenUploaded.IsCompletedSuccessfully)
                return;

            _uploads.Remove(uploadsItem);
            using (Invalidation.Begin()) {
                _ = _triggers.OnMediaUploadsChanged(chatEntryId);
                _ = _triggers.OnMediaUploadsChanged(uploadsItem.Item1);
            }
        }
    }

    private AttachmentUploads? GetMediaUploads(ChatEntryId chatEntryId)
    {
        _ = _triggers.OnMediaUploadsChanged(chatEntryId); // NOTE: completed synchronously
        lock (_chatSendingMessagesLock) {
            var uploadsItem = _uploads.FirstOrDefault(c => Equals(c.Item1.PostedChatEntry?.Id, chatEntryId));
            return uploadsItem.Item2;
        }
    }

    private AttachmentUploads? GetMediaUploads(SendingMessage sendingMessage)
    {
        _ = _triggers.OnMediaUploadsChanged(sendingMessage);  // NOTE: completed synchronously
        lock (_chatSendingMessagesLock) {
            var uploadsItem = _uploads.FirstOrDefault(c => Equals(c.Item1, sendingMessage));
            return uploadsItem.Item2;
        }
    }
}
