using Cysharp.Text;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Internal;

namespace ActualChat.Db.MySql;

public class MySqlDbHintFormatter : DbHintFormatter
{
    public MySqlDbHintFormatter()
        => DbHintToSql = new Dictionary<DbHint, string>() {
            {DbLockingHint.KeyShare, "SHARE"},
            {DbLockingHint.Share, "SHARE"},
            {DbLockingHint.NoKeyUpdate, "UPDATE"},
            {DbLockingHint.Update, "UPDATE"},
            {DbWaitHint.NoWait, "NOWAIT"},
            {DbWaitHint.SkipLocked, "SKIP LOCKED"},
        };

    public override string FormatSelectSql(string tableName, ref MemoryBuffer<DbHint> hints)
    {
        var sb = ZString.CreateStringBuilder();
        try {
            sb.Append("SELECT * FROM ");
            FormatTableNameTo(ref sb, tableName);
            var isFirst = true;
            foreach (var hint in hints) {
                if (isFirst)
                    sb.Append(" FOR ");
                else
                    sb.Append(' ');
                sb.Append(FormatHint(hint));
                isFirst = false;
            }
            return sb.ToString();
        }
        finally {
            sb.Dispose();
        }
    }

    protected override void FormatTableNameTo(ref Utf16ValueStringBuilder sb, string tableName)
    {
        sb.Append("`");
        sb.Append(tableName);
        sb.Append("`");
    }
}
