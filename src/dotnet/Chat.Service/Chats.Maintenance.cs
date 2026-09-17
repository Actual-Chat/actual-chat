namespace ActualChat.Chat;

public partial class Chats
{
    public virtual async Task OnSetMaintenance(Chats_SetMaintenance command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Backend.Get(command.ChatId, cancellationToken).Require().ConfigureAwait(false);

        var mode = command.IsEnabled ? MaintenanceMode.System : MaintenanceMode.None;
        await Commander.Call(
            new MaintenancesBackend_Set(command.ChatId.ToMaintenanceKey(), mode),
            true, cancellationToken).ConfigureAwait(false);
    }
}
