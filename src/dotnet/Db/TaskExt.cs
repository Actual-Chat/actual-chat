namespace ActualChat.Db;

public static class TaskExt
{
    public static Task<int> RequireOneUpdated(this Task<int> updateDbTask)
        => RequireUpdated(updateDbTask, 1);

    public static async Task<int> RequireUpdated(this Task<int> updateDbTask, int expectedUpdateCount)
    {
        var updateCount = await updateDbTask.ConfigureAwait(false);
        if (updateCount < 1)
            throw StandardError.Constraint($"Expected {expectedUpdateCount} rows to be updated, though only {updateCount} rows were updated.");

        return updateCount;
    }

    public static async Task<int> RequireAtLeastOneUpdated(this Task<int> updateDbTask)
    {
        var updateCount = await updateDbTask.ConfigureAwait(false);
        if (updateCount < 1)
            throw StandardError.Constraint("Expected at least one row to be updated, though no rows were.");

        return updateCount;
    }
}
