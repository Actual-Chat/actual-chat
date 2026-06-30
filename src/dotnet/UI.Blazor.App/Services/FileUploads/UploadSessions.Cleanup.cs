namespace ActualChat.UI.Blazor.App.Services;

partial class UploadSessions
{
    private async Task Cleanup()
    {
        // We wait until all upload sessions have been requested by their consumers and their usage IDs have been updated.
        // After this, we'll see which upload sessions are unused and can be cleared.
        await Hub.SendingMessages.WhenStoredRequestsProcessed.ConfigureAwait(false);
        // Let's wait a bit to avoid increasing the load on the system while the application is starting.
        await Task.Delay(5000).ConfigureAwait(false);
        Log.LogDebug("About to cleanup upload sessions");
        try {
            await _repo.Flush().ConfigureAwait(false);

            var items = await _repo.GetAll().ConfigureAwait(false);
            var corruptedItemIds = new List<string>();
            var staleItems = new List<KeyValuePair<string, UploadSessionSnapshot>>();
            foreach (var item in items) {
                var uploadSession = (UploadSessionSnapshot?)item.Value;
                if (uploadSession is null) {
                    corruptedItemIds.Add(item.Key);
                    continue;
                }

                // Stale = persisted but with no in-memory session, i.e. an orphan left by a prior run.
                if (!CheckIfActive(uploadSession.SessionId))
                    staleItems.Add(item);
            }

            if (corruptedItemIds.Count == 0 && staleItems.Count == 0) {
                Log.LogDebug("No upload sessions to cleanup");
                return;
            }

            Log.LogDebug("About to delete {Count} corrupted upload sessions", corruptedItemIds.Count);
            foreach (var id in corruptedItemIds)
                await _repo.Delete(id).ConfigureAwait(false);
            await _repo.Flush().ConfigureAwait(false);

            Log.LogDebug("About to delete {Count} stale upload sessions", staleItems.Count);
            foreach (var item in staleItems) {
                try {
                    var cleanup = AttachmentCleanupFactory.ForStaleUploadSession(this, item.Key);
                    await cleanup.Cleanup().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Failed to cleanup upload session '{SessionId}'", item.Key);
                }
            }
            await _repo.Flush().ConfigureAwait(false);
            Log.LogDebug("Completed upload sessions cleanup");
        }
        catch(Exception ex) {
            Log.LogError(ex, "Failed to cleanup upload sessions");
        }
    }
}
