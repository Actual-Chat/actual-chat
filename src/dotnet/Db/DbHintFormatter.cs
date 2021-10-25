using System.Text;
using Cysharp.Text;

namespace ActualChat.Db;

public static class DbHintFormatter
{
    public static string Format(this DbLockingHint lockingHint)
        => lockingHint switch {
            DbLockingHint.None => "",
            DbLockingHint.Update => "UPDATE",
            DbLockingHint.NoKeyUpdate => "NO KEY UPDATE",
            DbLockingHint.Share => "SHARE",
            DbLockingHint.KeyShare => "KEY SHARE",
            _ => throw new ArgumentOutOfRangeException(nameof(lockingHint), lockingHint, null),
        };

    public static string Format(this DbWaitHint waitHint)
        => waitHint switch {
            DbWaitHint.None => "",
            DbWaitHint.NoWait => "NOWAIT",
            DbWaitHint.SkipLocked => "SKIP LOCKED",
            _ => throw new ArgumentOutOfRangeException(nameof(waitHint), waitHint, null),
        };

    public static void AppendTo(
        ref Utf16ValueStringBuilder target,
        DbLockingHint lockingHint,
        DbWaitHint waitHint,
        params string[] tableNames)
    {
        if (lockingHint == DbLockingHint.None)
            return;
        target.Append(" FOR ");
        target.Append(lockingHint.Format());
        var isFirstTable = true;
        foreach (var tableName in tableNames) {
            target.Append(isFirstTable ? " \"" : ", \"");
            target.Append(tableName);
            target.Append("\"");
            isFirstTable = false;
        }
        if (waitHint != DbWaitHint.None) {
            target.Append(' ');
            target.Append(waitHint.Format());
        }
    }
}
