namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessagesTriggers : IComputeService, IHasIsDisposed
{
    [ComputeMethod]
    public virtual Task<Unit> OnNewMessagesChanged(ChatId chatId)
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    public virtual Task<Unit> OnEditMessageChanged(ChatEntryId chatEntryId)
        => ActualLab.Async.TaskExt.UnitTask;

    public bool IsDisposed { get; set; }
}
