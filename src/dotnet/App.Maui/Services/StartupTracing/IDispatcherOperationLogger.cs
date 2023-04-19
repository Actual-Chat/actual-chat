namespace ActualChat.App.Maui.Services.StartupTracing;

internal interface IDispatcherOperationLogger
{
    void OnBeforeOperation();
    void OnAfterOperation();
}
