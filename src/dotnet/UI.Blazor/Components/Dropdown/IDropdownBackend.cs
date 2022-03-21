namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Is used from js, part of workaround <see href="https://github.com/dotnet/aspnetcore/issues/9974"/>
/// </summary>
public interface IDropdownBackend
{
    Task HideContent();
}
