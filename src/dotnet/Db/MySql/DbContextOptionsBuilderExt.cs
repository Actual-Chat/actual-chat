using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db.MySql;

public static class DbContextOptionsBuilderExt
{
    public static DbContextOptionsBuilder UseMySqlHintFormatter(this DbContextOptionsBuilder dbContext)
        => dbContext.UseHintFormatter<MySqlDbHintFormatter>();
}
