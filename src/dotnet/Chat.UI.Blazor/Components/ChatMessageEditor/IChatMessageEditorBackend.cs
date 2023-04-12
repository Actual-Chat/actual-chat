namespace ActualChat.Chat.UI.Blazor.Components;

public interface IChatMessageEditorBackend
{
    bool AddAttachment(ChatId chatId, int id, string url, string? fileName, string? fileType, int length);
    void CloseNotifyPanel();
    void UpdateProgress(ChatId chatId, int id, int progress);
    void CompleteUpload(ChatId chatId, int id, MediaId mediaId);
}
