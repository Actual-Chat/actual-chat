namespace ActualChat.Testing.Host;

public static class MaintenanceOperations
{
    public static Task SetChatMaintenance(this IWebTester tester, ChatId chatId, bool isEnabled)
    {
        // A chat of a Place shares the root's key, so it's added to or removed from the key's targets
        var mode = isEnabled ? MaintenanceMode.System : MaintenanceMode.None;
        var target = chatId is PlaceChatId { IsRoot: false } ? chatId.ToMaintenanceTarget() : null;
        var targetDiff = target is null
            ? SetDiff<string>.Unchanged
            : isEnabled ? new SetDiff<string>([target]) : new SetDiff<string>([], [target]);
        var setMaintenanceCmd = new MaintenancesBackend_Set(chatId.ToMaintenanceKey(), mode) {
            TargetDiff = targetDiff,
        };
        return tester.Commander.Call(setMaintenanceCmd);
    }
}
