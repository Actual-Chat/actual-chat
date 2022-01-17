namespace ActualChat.UI.Blazor.Components;

public record AccountInfo(string Name, string Picture);

public interface IAccountInfoProvider
{
    public Task<AccountInfo?> GetAccountInfo(User user, CancellationToken cancellationToken);
}
