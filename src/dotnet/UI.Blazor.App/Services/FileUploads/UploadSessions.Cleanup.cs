namespace ActualChat.UI.Blazor.App.Services;

partial class UploadSessions
{
    private async Task Cleanup()
    {
        // We wait until all upload sessions have been requested by their consumers and their usage IDs have been updated.
        // After this, we'll see which upload sessions are unused and can be cleared.
        await Hub.SendingMessages.WhenReady.ConfigureAwait(false);
        // Let's wait a bit to avoid increasing the load on the system while the application is starting.
        await Task.Delay(5000).ConfigureAwait(false);
        Log.LogDebug("About to cleanup upload sessions");
        try {
            await _repository.Flush().ConfigureAwait(false);

            var items = await _repository.GetAll().ConfigureAwait(false);
            var corruptedItemIds = new List<string>();
            var staleItems = new List<KeyValuePair<string, UploadSession>>();
            foreach (var item in items) {
                var uploadSession = (UploadSession?)item.Value;
                if (uploadSession is null) {
                    corruptedItemIds.Add(item.Key);
                    continue;
                }

                if (!OrdinalEquals(UsageId, uploadSession.UsageId))
                    staleItems.Add(item);
            }

            if (corruptedItemIds.Count == 0 && staleItems.Count == 0) {
                Log.LogDebug("No upload sessions to cleanup");
                return;
            }

            Log.LogDebug("About to delete {Count} corrupted upload sessions", corruptedItemIds.Count);
            foreach (var id in corruptedItemIds)
                await _repository.Delete(id).ConfigureAwait(false);
            await _repository.Flush().ConfigureAwait(false);

            Log.LogDebug("About to delete {Count} stale upload sessions", staleItems.Count);
            foreach (var item in staleItems) {
                try {
                    var cleanup = AttachmentCleanupFactory.ForUploadSession(this, item.Key);
                    await cleanup.Cleanup().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Failed to cleanup upload session '{SessionId}'", item.Key);
                }
            }
            await _repository.Flush().ConfigureAwait(false);
            Log.LogDebug("Completed upload sessions cleanup");
        }
        catch(Exception ex) {
            Log.LogError(ex, "Failed to cleanup upload sessions");
        }
    }
}
