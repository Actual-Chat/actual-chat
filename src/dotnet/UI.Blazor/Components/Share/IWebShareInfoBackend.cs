namespace ActualChat.UI.Blazor.Components;

public interface IWebShareInfoBackend
{
    public sealed record InitResult(
        bool CanShareText,
        bool CanShareLink);
}
