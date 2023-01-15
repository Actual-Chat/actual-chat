using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db;

public static class DbContextExt
{
    // This is a helper method allowing to debug exceptions thrown from SaveChangesAsync
    public static async Task SaveChangesAsync(this DbContext dbContext, ILogger? log, CancellationToken cancellationToken = default)
    {
        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            log?.LogError(e, "SaveChangesAsync failed");
            throw;
        }
    }
}
