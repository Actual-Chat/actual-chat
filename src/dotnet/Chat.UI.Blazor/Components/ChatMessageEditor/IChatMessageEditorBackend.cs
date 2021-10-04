namespace ActualChat.Chat.UI.Blazor.Internal;

/// <summary>
/// Is used from js, part of workaround <see href="https://github.com/dotnet/aspnetcore/issues/9974"/>
/// </summary>
public interface IChatMessageEditorBackend
{
    void SetMessage(string message);
}