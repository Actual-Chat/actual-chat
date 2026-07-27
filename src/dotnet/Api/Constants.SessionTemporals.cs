namespace ActualChat;

public static partial class Constants
{
    // Session temporals share one per-session hash: keys written through the client-facing API
    // go under ClientKeyPrefix, every other key is server-only.

    public static class SessionTemporals
    {
        public const string ClientKeyPrefix = "c.";
        public const string SignInErrorKey = "SignInError";
        public const string PendingRegistrationKey = "PendingRegistration";
        // Written to SignInErrorKey when the user cancels a registration
        // confirmation prompt. The UI uses this exact string to detect
        // a cancel and reset the sign-in form.
        public const string SignInCanceledMessage = "Sign-in canceled.";
        private static readonly HashSet<string> ServerKeys = [SignInErrorKey, PendingRegistrationKey];

        public static bool IsServerKey(string key)
            => ServerKeys.Contains(key);
        public static string ToClientKey(string key)
            => ClientKeyPrefix + key;
    }
}
