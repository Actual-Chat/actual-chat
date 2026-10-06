namespace ActualChat.Chat;

public partial class Chats
{
    public virtual async Task OnSetMaintenance(Chats_SetMaintenance command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Backend.Get(command.ChatId, cancellationToken).Require().ConfigureAwait(false);

        var key = command.ChatId.ToMaintenanceKey();
        var target = command.ChatId is PlaceChatId { IsRoot: false } ? command.ChatId.ToMaintenanceTarget() : null;
        var mode = command.IsEnabled ? MaintenanceMode.System : MaintenanceMode.None;
        var targets = ApiArray<string>.Empty;
        if (target is not null) {
            // A chat in a Place shares the root's row: update its targets, keeping the other chats'
            var current = await MaintenancesBackend.Get(key, cancellationToken).ConfigureAwait(false);
            if (current.Mode != MaintenanceMode.None && current.Targets.IsEmpty)
                throw StandardError.Constraint("The whole Place is in maintenance mode.");

            targets = command.IsEnabled
                ? current.Targets.WithOrSkip(target)
                : current.Targets.Without(target);
            mode = targets.IsEmpty ? MaintenanceMode.None : MaintenanceMode.System;
        }

        var setMaintenanceCmd = new MaintenancesBackend_Set(key, mode) { Targets = targets };
        await Commander.Call(setMaintenanceCmd, cancellationToken).ConfigureAwait(false);
    }
}
