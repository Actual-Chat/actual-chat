namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentListEventsListener
{
    Task AttachmentsRemoved(AttachmentList attachmentList, Attachment[] attachments);
    Task RestartUploadRequested(AttachmentList attachmentList, Attachment attachment);
}
