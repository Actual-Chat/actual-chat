namespace ActualChat.UI.Blazor.Services;

public interface IUserActivityUIBackend
{
    void OnInteraction(double willBeActiveForMs);
}
