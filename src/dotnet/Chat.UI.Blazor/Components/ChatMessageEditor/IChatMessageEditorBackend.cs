namespace ActualChat.Chat.UI.Blazor.Components;

/// <summary>
/// Is used from JS, part of workaround <see href="https://github.com/dotnet/aspnetcore/issues/9974"/>
/// </summary>
public interface IChatMessageEditorBackend
{
    void UpdateClientSideState(string? text);
    Task Post(string? text = null);
}
