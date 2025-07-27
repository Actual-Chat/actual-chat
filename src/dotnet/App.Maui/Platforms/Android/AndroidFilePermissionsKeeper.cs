using Android.Content;

namespace ActualChat.App.Maui;

internal static class AndroidFilePermissionsKeeper
{
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, State> States = new (StringComparer.Ordinal);
    private static ILogger Log => StaticLog.For(typeof(AndroidFilePermissionsKeeper));

    public static void Register(string uri, AndroidFileProviderImpl androidFileProviderImpl)
    {
        lock (Lock) {
            if (!States.TryGetValue(uri, out var state)) {
                state = new State();
                States[uri] = state;
                var permissions = Platform.AppContext.ContentResolver!
                    .PersistedUriPermissions.ToArray();
                var hasReadPermission = permissions.Any(c => c.IsReadPermission && OrdinalEquals(c.Uri!.ToString(), uri));
                state.HasReadPermission = hasReadPermission;
                Log.LogDebug("Registering file provider for uri '{Uri}', app has persistent read permission: {HasPermission}", uri, hasReadPermission);
            }
            state.Holders.Add(androidFileProviderImpl);
        }
    }

    public static void TakeReadPermission(string uri, AndroidFileProviderImpl androidFileProviderImpl)
    {
        lock (Lock) {
            if (!States.TryGetValue(uri, out var state)) {
                state = new State();
                States[uri] = state;
            }

            state.Holders.Add(androidFileProviderImpl);
            if (state.HasReadPermission)
                return;

            try {
                Platform.AppContext.ContentResolver!
                    .TakePersistableUriPermission(Android.Net.Uri.Parse(uri)!, ActivityFlags.GrantReadUriPermission);
                state.HasReadPermission = true;
                Log.LogDebug("Took persistent read permission for uri '{Uri}'", uri);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to take persistent read permission for uri '{Uri}'", uri);
            }
        }
    }

    public static void ReleaseReadPermission(string uri, AndroidFileProviderImpl androidFileProviderImpl)
    {
        lock (Lock) {
            if (!States.TryGetValue(uri, out var state)) {
                state = new State();
                States[uri] = state;
            }

            state.Holders.Remove(androidFileProviderImpl);
            if (!state.HasReadPermission || state.Holders.Count > 0)
                return;

            Platform.AppContext.ContentResolver!
                .ReleasePersistableUriPermission(Android.Net.Uri.Parse(uri)!, ActivityFlags.GrantReadUriPermission);
            state.HasReadPermission = false;
            Log.LogDebug("Released persistent read permission for uri '{Uri}'", uri);
        }
    }

    private class State()
    {
        public List<AndroidFileProviderImpl> Holders { get; } = new();
        public bool HasReadPermission { get; set; }
    }
}
